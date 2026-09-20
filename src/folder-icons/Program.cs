using FolderIcons;

const string Usage = """
    folder-icons [--config <file>] [--once]

    Keeps Dolphin per-folder icons (.directory files) in sync with a YAML rules file.
      --config <file>  rules file (default: ~/.config/folder-icons.yaml)
      --once           apply the rules to existing folders and exit instead of watching
    Send SIGHUP (systemctl --user reload folder-icons) to reload the rules while running.
    """;

if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Usage); return 0; }

var configIndex = Array.IndexOf(args, "--config");
var configPath = configIndex >= 0 && configIndex + 1 < args.Length
    ? Config.ExpandPath(args[configIndex + 1])
    : Config.ExpandPath("~/.config/folder-icons.yaml");

Config config;
try { config = Config.Load(configPath); }
catch (Exception e)
{
    Console.Error.WriteLine($"cannot load {configPath}: {e.Message}");
    return 1;
}

var service = new FolderIconService(configPath, config);
if (args.Contains("--once"))
{
    service.RunOnce();
    return 0;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();
await service.RunAsync(cts.Token);
return 0;
