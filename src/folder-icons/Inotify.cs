using System.Runtime.InteropServices;
using System.Text;

namespace FolderIcons;

internal readonly record struct InotifyEvent(int Wd, uint Mask, string Name);

/// <summary>Minimal inotify wrapper. .NET's FileSystemWatcher can't skip subtrees, so watches are added folder by folder.</summary>
internal sealed class Inotify : IDisposable
{
    public const uint Create = 0x100, MovedTo = 0x80, Delete = 0x200, MovedFrom = 0x40, Overflow = 0x4000, Ignored = 0x8000, IsDir = 0x40000000;
    private const int NonBlock = 0x800, CloseOnExec = 0x80000, PollIn = 1;

    private readonly int _fd;

    public Inotify()
    {
        _fd = inotify_init1(NonBlock | CloseOnExec);
        if (_fd < 0) throw new InvalidOperationException($"inotify_init1 failed: {Marshal.GetLastPInvokeErrorMessage()}");
    }

    /// <summary>Watches <paramref name="path"/> for entries appearing in or disappearing from it. Returns the watch descriptor, or -1 on failure.</summary>
    public int AddWatch(string path, out string error)
    {
        var wd = inotify_add_watch(_fd, path, Create | MovedTo | Delete | MovedFrom);
        error = wd < 0 ? Marshal.GetLastPInvokeErrorMessage() : "";
        return wd;
    }

    /// <summary>Blocks, delivering events to <paramref name="onEvent"/> until cancelled.</summary>
    public void Pump(Action<InotifyEvent> onEvent, CancellationToken ct)
    {
        var buf = new byte[64 * 1024];
        var fds = new PollFd[] { new() { Fd = _fd, Events = PollIn } };
        while (!ct.IsCancellationRequested)
        {
            if (poll(fds, 1, 500) <= 0) continue;
            var n = (int)read(_fd, buf, buf.Length);
            for (var off = 0; off < n;)
            {
                var wd = BitConverter.ToInt32(buf, off);
                var mask = BitConverter.ToUInt32(buf, off + 4);
                var len = (int)BitConverter.ToUInt32(buf, off + 12);
                var nameBytes = buf.AsSpan(off + 16, len);
                var end = nameBytes.IndexOf((byte)0);
                var name = Encoding.UTF8.GetString(end < 0 ? nameBytes : nameBytes[..end]);
                off += 16 + len;
                onEvent(new InotifyEvent(wd, mask, name));
            }
        }
    }

    public void Dispose() => close(_fd);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)] private static extern int inotify_init1(int flags);
    [DllImport("libc", SetLastError = true)] private static extern int inotify_add_watch(int fd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mask);
    [DllImport("libc", SetLastError = true)] private static extern int poll([In, Out] PollFd[] fds, ulong nfds, int timeout);
    [DllImport("libc", SetLastError = true)] private static extern nint read(int fd, byte[] buf, nint count);
    [DllImport("libc")] private static extern int close(int fd);
}
