namespace FolderIcons;

/// <summary>Makes sure a .gitignore lists the files this tool creates, so they never show up in git status.</summary>
internal static class GitIgnore
{
    public static void Ensure(string gitignore, IReadOnlyList<string> entries)
    {
        if (entries.Count == 0) return;
        try
        {
            var text = File.ReadAllText(gitignore);
            var lines = text.Split('\n').Select(l => l.Trim()).ToHashSet();
            var missing = entries.Where(e => !lines.Contains(e) && !lines.Contains("/" + e) && !lines.Contains("**/" + e)).ToList();
            if (missing.Count == 0) return;

            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var lead = text.Length > 0 && !text.EndsWith('\n') ? newline : "";
            File.AppendAllText(gitignore, lead + string.Join(newline, missing) + newline);
            Console.WriteLine($"gitignore +{string.Join(", ", missing)} {gitignore}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"skip {gitignore}: {e.Message}");
        }
    }
}
