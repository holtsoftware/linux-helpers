using System.Threading.Channels;

namespace FolderIcons;

/// <summary>Applies the configured icons to existing folders, then keeps watching for changes.</summary>
internal sealed class FolderIconService
{
    private readonly string _configPath;
    private Config _config;
    private IconResolver _resolver;
    private readonly Dictionary<int, string> _watches = [];
    private Inotify? _inotify;
    private int _folders;

    public FolderIconService(string configPath, Config config)
    {
        _configPath = configPath;
        _config = config;
        _resolver = new IconResolver(config);
    }

    /// <summary>One pass over the tree, without watching.</summary>
    public void RunOnce() => Scan();

    public async Task RunAsync(CancellationToken ct)
    {
        using var inotify = _inotify = new Inotify();
        var queue = Channel.CreateUnbounded<(bool Reload, InotifyEvent Event)>();
        using var _ = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGHUP,
            ctx => { ctx.Cancel = true; queue.Writer.TryWrite((true, default)); });

        Scan();
        var pump = Task.Factory.StartNew(
            () => inotify.Pump(e => queue.Writer.TryWrite((false, e)), ct),
            ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        try
        {
            await foreach (var (reload, e) in queue.Reader.ReadAllAsync(ct))
            {
                if (reload) Reload();
                else Handle(e);
            }
        }
        catch (OperationCanceledException) { }
        await pump;
    }

    private void Reload()
    {
        try
        {
            _config = Config.Load(_configPath);
            _resolver = new IconResolver(_config);
            Scan();
            Console.WriteLine("config reloaded");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Console.WriteLine($"reload failed, keeping previous config: {e.Message}");
        }
    }

    private void Scan()
    {
        _watches.Clear();
        _resolver.Clear();
        _folders = 0;
        Walk(_config.Root);
        Console.WriteLine($"{(_inotify is null ? "scanned" : "watching")} {_folders} folders under {_config.Root} with {_config.FolderIcon.Count} icon rules");
    }

    private void Walk(string dir)
    {
        _folders++;
        DirectoryFile.Apply(dir, _resolver.IconFor(dir));
        var gitignore = Path.Combine(dir, ".gitignore");
        if (File.Exists(gitignore)) GitIgnore.Ensure(gitignore, _config.GitIgnore);

        if (_inotify is not null)
        {
            var wd = _inotify.AddWatch(dir, out var error);
            if (wd < 0) Console.WriteLine($"watch failed {dir}: {error}");
            else _watches[wd] = dir;
        }
        foreach (var sub in SubFolders(dir))
        {
            if (_config.IsExcluded(Path.GetFileName(sub))) DirectoryFile.Apply(sub, _resolver.IconFor(sub, excluded: true));
            else Walk(sub);
        }
    }

    private static IEnumerable<string> SubFolders(string dir)
    {
        try
        {
            return Directory.EnumerateDirectories(dir)
                .Where(d => !File.GetAttributes(d).HasFlag(FileAttributes.ReparsePoint))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Handle(InotifyEvent e)
    {
        if ((e.Mask & Inotify.Overflow) != 0) { Scan(); return; }
        if ((e.Mask & Inotify.Ignored) != 0) { _watches.Remove(e.Wd); return; }
        if (!_watches.TryGetValue(e.Wd, out var parent)) return;

        // A marker file/folder (a .csproj, .git, ...) appeared or vanished: its folder and everything below may change icon.
        if (_resolver.IsMarkerName(e.Name))
        {
            if (_resolver.MarkersChanged(parent)) Walk(parent);
            return;
        }
        if ((e.Mask & (Inotify.Delete | Inotify.MovedFrom)) != 0) return;

        var path = Path.Combine(parent, e.Name);
        if ((e.Mask & Inotify.IsDir) == 0)
        {
            if (e.Name == ".gitignore") GitIgnore.Ensure(path, _config.GitIgnore);
            return;
        }
        if (!Directory.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) return;
        if (_config.IsExcluded(e.Name)) DirectoryFile.Apply(path, _resolver.IconFor(path, excluded: true));
        else Walk(path);
    }
}
