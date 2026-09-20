using System.IO.Enumeration;

namespace FolderIcons;

/// <summary>Works out which icon a folder gets by evaluating every folderIcon rule; the lowest priority number wins.</summary>
internal sealed class IconResolver(Config config)
{
    /// <summary>Folder -> names of the entries directly inside it, so ancestors are only listed once per scan.</summary>
    private readonly Dictionary<string, string[]> _entries = [];

    public void Clear() => _entries.Clear();

    public bool IsMarkerName(string name) =>
        config.FolderIcon.Any(r => r.WatchMarkers.Any(m => Glob(m, name)));

    /// <summary>Re-reads <paramref name="dir"/> and reports whether the set of rules it is a marker folder for changed.</summary>
    public bool MarkersChanged(string dir)
    {
        var before = MarkerRules(dir);
        _entries.Remove(dir);
        return !before.SequenceEqual(MarkerRules(dir));
    }

    /// <param name="excluded">True for folders that are not walked (matched by "exclude"). They only get an icon from a
    /// "folder" rule that names them exactly, so hidden and generated folders below a project are left alone.</param>
    public string? IconFor(string dir, bool excluded = false)
    {
        FolderIconRule? best = null;
        foreach (var rule in config.FolderIcon)
        {
            if (best is not null && rule.Priority >= best.Priority) continue; // ties keep the earlier rule
            if (Matches(rule, dir, excluded)) best = rule;
        }
        return best?.Icon;
    }

    private bool Matches(FolderIconRule rule, string dir, bool excluded)
    {
        if (excluded) return rule.Type == "folder" && dir == rule.Path && rule.Self;
        if (rule.Type == "folder") return Below(rule.Path!, dir, rule) is not null;
        if (rule.Path is not null && dir != rule.Path && !dir.StartsWith(rule.Path + "/", StringComparison.Ordinal)) return false;

        // Look for the nearest marker folder at or above dir, no further away than the rule's depth.
        var a = dir;
        for (var level = 0; a is not null && (a == config.Root || a.StartsWith(config.Root + "/", StringComparison.Ordinal)); level++)
        {
            if (rule.Depth != -1 && level > rule.Depth) return false;
            if (IsAnchor(a, rule) && (level > 0 || rule.Self)) return true;
            a = Path.GetDirectoryName(a);
        }
        return false;
    }

    /// <summary>The distance from <paramref name="anchor"/> down to <paramref name="dir"/> if the rule reaches it, else null.</summary>
    private static int? Below(string anchor, string dir, FolderIconRule rule)
    {
        if (dir == anchor) return rule.Self ? 0 : null;
        if (!dir.StartsWith(anchor + "/", StringComparison.Ordinal)) return null;
        var level = dir[(anchor.Length + 1)..].Count(c => c == '/') + 1;
        return rule.Depth == -1 || level <= rule.Depth ? level : null;
    }

    private int[] MarkerRules(string dir) =>
        [.. config.FolderIcon.Select((r, i) => (r, i)).Where(t => HasAny(dir, [.. t.r.WatchMarkers])).Select(t => t.i)];

    /// <summary>Whether <paramref name="dir"/> is where the rule starts: a marker folder, or a folder with a matching name.</summary>
    private bool IsAnchor(string dir, FolderIconRule rule) =>
        rule.Type == "name" ? NameMatches(dir, rule) : HasAny(dir, rule.Markers);

    private bool NameMatches(string dir, FolderIconRule rule) =>
        rule.Names.Any(n => Glob(n, Path.GetFileName(dir)))
        && (rule.WithinMarkers.Count == 0 || (Path.GetDirectoryName(dir) is { } parent && HasAny(parent, rule.WithinMarkers)));

    private bool HasAny(string dir, IReadOnlyList<string> patterns) =>
        patterns.Count > 0 && Entries(dir).Any(n => patterns.Any(m => Glob(m, n)));

    private string[] Entries(string dir)
    {
        if (_entries.TryGetValue(dir, out var names)) return names;
        try
        {
            names = [.. Directory.EnumerateFileSystemEntries(dir).Select(p => Path.GetFileName(p))];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            names = [];
        }
        return _entries[dir] = names;
    }

    private static bool Glob(string pattern, string name) =>
        FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: false);
}
