using System.Net.Sockets;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Platform;

/// <summary>
/// Single-instance guard backed by an exclusive lock file, plus a Unix domain socket that lets a
/// second launch ask the first (already-running) instance to activate itself. Both live under a
/// per-user runtime directory rather than a predictable shared path in /tmp: on a multi-user host,
/// a `/tmp/yay_see_sharp.lock`-style path could be pre-created or raced by another user, and
/// `$XDG_RUNTIME_DIR` is already per-user (created by the session manager, not world-writable).
/// </summary>
public sealed class FileLockSingleInstanceService : ISingleInstanceService
{
    private readonly string _lockFilePath;
    private readonly string _socketPath;

    private FileStream? _lockStream;
    private Socket? _listenerSocket;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public FileLockSingleInstanceService(string? runtimeDirectory = null)
    {
        var directory = runtimeDirectory ?? ResolveDefaultRuntimeDirectory();
        Directory.CreateDirectory(directory);
        TrySetOwnerOnlyPermissions(directory);

        _lockFilePath = Path.Combine(directory, "instance.lock");
        _socketPath = Path.Combine(directory, "activate.sock");
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquire()
    {
        if (_lockStream is not null)
        {
            return true;
        }

        try
        {
            _lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return false;
        }

        StartActivationListener();
        return true;
    }

    public bool TryActivateExisting()
    {
        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            client.Connect(new UnixDomainSocketEndPoint(_socketPath));
            client.Send("activate"u8.ToArray());
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _listenerCts?.Cancel();
        try
        {
            _listenerSocket?.Close();
        }
        catch (SocketException)
        {
        }

        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _listenerSocket?.Dispose();
        _listenerCts?.Dispose();
        _listenerSocket = null;
        _listenerCts = null;
        _listenerTask = null;

        _lockStream?.Dispose();
        _lockStream = null;

        // Deliberately not deleting the lock file: the filesystem exclusive-open lock this class
        // relies on is released as soon as the stream closes (above), regardless of whether the
        // file itself still exists. Deleting it here would race a concurrently starting instance
        // that just recreated the same path — this process could delete a lock file that already
        // belongs to a different, live FileStream.
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }
    }

    private void StartActivationListener()
    {
        // A stale socket file can be left behind after an unclean shutdown (e.g. SIGKILL); Bind
        // fails with AddressInUse against a leftover path even though nothing is listening.
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }

        _listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenerSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listenerSocket.Listen();

        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => AcceptLoopAsync(_listenerSocket, _listenerCts.Token));
    }

    private async Task AcceptLoopAsync(Socket listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptAsync(cancellationToken);
                var buffer = new byte[64];
                await client.ReceiveAsync(buffer, cancellationToken);
                ActivationRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException)
        {
            // Listener socket was closed from Dispose while AcceptAsync was pending.
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string ResolveDefaultRuntimeDirectory()
    {
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var baseDirectory = string.IsNullOrWhiteSpace(xdgRuntimeDir) ? Path.GetTempPath() : xdgRuntimeDir;
        return Path.Combine(baseDirectory, "yay_see_sharp");
    }

    private static void TrySetOwnerOnlyPermissions(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
