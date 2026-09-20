using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FolderIcons;

/// <summary>
/// One folderIcon rule. <see cref="Type"/> "folder" matches the folder at <see cref="Path"/>; "name" matches folders called one of
/// <see cref="Names"/> (only when their parent contains one of <see cref="WithinMarkers"/>, if any); any other type matches folders
/// containing one of its <see cref="Markers"/>. Either way the icon goes to the matched folder (if <see cref="Self"/>) and to
/// the folders below it down to <see cref="Depth"/> (-1 = all, 0 = none). A lower <see cref="Priority"/> number wins.
/// </summary>
internal sealed record FolderIconRule(
    string Type, string? Path, string Icon, bool Self, int Depth, int Priority, IReadOnlyList<string> Markers,
    IReadOnlyList<string> Names, IReadOnlyList<string> WithinMarkers)
{
    /// <summary>Every file/folder name whose appearance or removal can change what this rule matches.</summary>
    public IEnumerable<string> WatchMarkers => Markers.Concat(WithinMarkers);
}

internal sealed class Config
{
    private static readonly Dictionary<string, string[]> BuiltInMarkers = new()
    {
        ["csproj"] = ["*.csproj"],
        ["c++"] = ["CMakeLists.txt", "Makefile", "*.cpp", "*.cc", "*.cxx", "*.vcxproj"],
        ["git"] = [".git"],
    };

    public required string Root { get; init; }
    public required IReadOnlyList<string> Exclude { get; init; }
    /// <summary>In file order, which breaks priority ties.</summary>
    public required IReadOnlyList<FolderIconRule> FolderIcon { get; init; }
    /// <summary>Lines every .gitignore that is found must contain.</summary>
    public required IReadOnlyList<string> GitIgnore { get; init; }

    public static Config Load(string file)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var raw = deserializer.Deserialize<ConfigFile?>(File.ReadAllText(file)) ?? new ConfigFile();

        var rules = new List<FolderIconRule>();
        foreach (var (r, i) in raw.Rules.FolderIcon.Select((r, i) => (r, i + 1)))
        {
            string Fail(string why) => $"rules.folderIcon[{i}]: {why}";
            if (string.IsNullOrWhiteSpace(r.Type)) throw new InvalidDataException(Fail("'type' is required"));
            if (string.IsNullOrWhiteSpace(r.Icon)) throw new InvalidDataException(Fail("'icon' is required"));

            int depth;
            if (r.Depth.Equals("all", StringComparison.OrdinalIgnoreCase)) depth = -1;
            else if (!int.TryParse(r.Depth, out depth) || depth < 0)
                throw new InvalidDataException(Fail($"'depth' must be a number >= 0 or \"all\", got '{r.Depth}'"));

            string[] markers = [], within = [];
            if (r.Type == "folder")
            {
                if (string.IsNullOrWhiteSpace(r.Path)) throw new InvalidDataException(Fail("type 'folder' needs a 'path'"));
            }
            else if (r.Type == "name")
            {
                if (r.Names is not { Count: > 0 }) throw new InvalidDataException(Fail("type 'name' needs a 'names' list"));
                if (r.Within is not null)
                {
                    var custom = raw.Rules.FolderIcon.FirstOrDefault(o => o.Type == r.Within && o.Markers is { Count: > 0 })?.Markers;
                    if (custom is not null) within = [.. custom];
                    else if (!BuiltInMarkers.TryGetValue(r.Within, out within!))
                        throw new InvalidDataException(Fail($"'within: {r.Within}' is not a built-in type ({string.Join(", ", BuiltInMarkers.Keys)}) or a type with markers"));
                }
            }
            else if (r.Markers is { Count: > 0 }) markers = [.. r.Markers];
            else if (!BuiltInMarkers.TryGetValue(r.Type, out markers!))
                throw new InvalidDataException(Fail(
                    $"unknown type '{r.Type}' - use folder, {string.Join(", ", BuiltInMarkers.Keys)}, or give a 'markers' list"));

            rules.Add(new FolderIconRule(r.Type, r.Path is null ? null : ExpandPath(r.Path), r.Icon, r.Self, depth,
                r.Priority ?? int.MaxValue, markers, r.Type == "name" ? [.. r.Names!] : [], within));
        }

        return new Config
        {
            Root = ExpandPath(raw.Root),
            Exclude = raw.Exclude,
            FolderIcon = rules,
            GitIgnore = raw.Rules.GitIgnore.Select(g => g.File).Where(f => !string.IsNullOrWhiteSpace(f)).ToList(),
        };
    }

    public bool IsExcluded(string name) =>
        Exclude.Any(p => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: false));

    public static string ExpandPath(string path)
    {
        if (path == "~") path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        else if (path.StartsWith("~/", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        var full = Path.GetFullPath(path);
        return full.Length > 1 ? full.TrimEnd('/') : full;
    }

    private sealed class ConfigFile
    {
        public string Root { get; set; } = "~";
        public List<string> Exclude { get; set; } = [".*", "node_modules"];
        public RulesFile Rules { get; set; } = new();
    }

    private sealed class RulesFile
    {
        public List<GitIgnoreFile> GitIgnore { get; set; } = [];
        public List<FolderIconFile> FolderIcon { get; set; } = [];
    }

    private sealed class GitIgnoreFile
    {
        public string File { get; set; } = "";
    }

    private sealed class FolderIconFile
    {
        public string Type { get; set; } = "";
        public string? Path { get; set; }
        public string Icon { get; set; } = "";
        public bool Self { get; set; } = true;
        public string Depth { get; set; } = "1";
        public int? Priority { get; set; }
        public List<string>? Markers { get; set; }
        public List<string>? Names { get; set; }
        public string? Within { get; set; }
    }
}
