namespace FolderIcons;

/// <summary>Reads and writes the ".directory" file Dolphin uses for a per-folder icon.</summary>
internal static class DirectoryFile
{
    private const string Mark = "# managed by folder-icons";

    /// <summary>Makes <paramref name="dir"/>'s .directory file match <paramref name="icon"/> (null = no icon).</summary>
    /// <remarks>Only files this tool wrote (or an older plain "[Desktop Entry] + Icon=" file) are ever changed or removed.</remarks>
    public static void Apply(string dir, string? icon)
    {
        var file = Path.Combine(dir, ".directory");
        try
        {
            var current = File.Exists(file) ? File.ReadAllText(file) : null;
            if (icon is not null)
            {
                var want = $"{Mark}\n[Desktop Entry]\nIcon={icon}\n";
                if (current is null || (IsManaged(current) && current != want))
                {
                    File.WriteAllText(file, want);
                    Console.WriteLine($"icon {icon} {dir}");
                }
            }
            else if (current is not null && current.Contains(Mark))
            {
                File.Delete(file);
                Console.WriteLine($"removed icon from {dir}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"skip {dir}: {e.Message}");
        }
    }

    private static bool IsManaged(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        return lines.Contains(Mark) || (lines.Length == 2 && lines[0] == "[Desktop Entry]" && lines[1].StartsWith("Icon="));
    }
}
