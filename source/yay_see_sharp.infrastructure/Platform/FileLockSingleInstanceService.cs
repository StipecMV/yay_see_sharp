using System.Net.Sockets;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Platform;

/// <summary>
/// Single-instance guard backed by an exclusive lock file, plus a Unix domain socket that lets a
/// second launch ask the first (already-running) instance to activate itself. Both live under a
/// per-user runtime directory rather than a predictable shared path in /tmp: on a multi-user host,
/// a `/tmp/yay_see_sharp.lock`-style path could be pre-created or raced by another user, and
/// `$XDG_RUNTIME_DIR` is already per-user (created by the session manager, not world-writable).
///
/// <see cref="TryAcquire"/> is transactional: acquiring the lock file and starting the activation
/// listener either both succeed or neither does. A listener failure after the lock was already
/// taken releases it before returning false, so a broken IPC listener never masquerades as "another
/// instance is running" and never leaves this process holding an inert lock.
/// </summary>
public sealed class FileLockSingleInstanceService : ISingleInstanceService
{
    private readonly string _lockFilePath;
    private readonly string _socketPath;
    private readonly bool _runtimeDirectoryIsOwnerOnly;

    private FileStream? _lockStream;
    private Socket? _listenerSocket;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public FileLockSingleInstanceService(string? runtimeDirectory = null)
    {
        var directory = runtimeDirectory ?? ResolveDefaultRuntimeDirectory();
        Directory.CreateDirectory(directory);
        _runtimeDirectoryIsOwnerOnly = EnsureOwnerOnlyPermissions(directory);

        _lockFilePath = Path.Combine(directory, "instance.lock");
        _socketPath = Path.Combine(directory, "activate.sock");
    }

    public event EventHandler? ActivationRequested;

    public string? LastFailureReason { get; private set; }

    public bool TryAcquire()
    {
        if (_lockStream is not null)
        {
            return true;
        }

        LastFailureReason = null;

        // On Linux, a runtime directory this process couldn't confirm as owner-only (e.g. it
        // pre-existed with foreign/world-writable permissions and this process wasn't its owner,
        // so the chmod attempt in the constructor couldn't have fixed it) is never trusted, even
        // if the lock file inside it would otherwise open fine — the whole point of the per-user
        // runtime directory is that another user cannot plant or read files there.
        if (OperatingSystem.IsLinux() && !_runtimeDirectoryIsOwnerOnly)
        {
            LastFailureReason =
                $"Runtime directory '{Path.GetDirectoryName(_lockFilePath)}' does not have owner-only permissions " +
                "(it may be pre-existing and owned by another user) — refusing to use it for single-instance locking.";
            return false;
        }

        FileStream lockStream;
        try
        {
            lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            // Expected, common case: another instance already holds the lock. Not a
            // LastFailureReason-worthy problem — this is exactly what single-instancing is for.
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            LastFailureReason = $"Could not open lock file '{_lockFilePath}': {ex.Message}";
            return false;
        }

        if (!StartActivationListener(out var listenerFailureReason))
        {
            LastFailureReason = listenerFailureReason;
            lockStream.Dispose();
            return false;
        }

        _lockStream = lockStream;
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

        // Socket cleanup happens BEFORE the lock stream is released (NEW-02): while this process
        // still holds the lock, no other process can acquire it and rebind `activate.sock`, so
        // deleting our own socket file here can never race a newer live instance's endpoint out
        // from under it. Deleting it *after* releasing the lock could: a new instance could
        // acquire the lock and bind a fresh socket in the window between this process's lock
        // release and its (delayed) socket delete, and that delete would then destroy the new
        // instance's live endpoint instead of this dead one's.
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }

        // Deliberately not deleting the lock file: the filesystem exclusive-open lock this class
        // relies on is released as soon as the stream closes (below), regardless of whether the
        // file itself still exists. Deleting it here would race a concurrently starting instance
        // that just recreated the same path — this process could delete a lock file that already
        // belongs to a different, live FileStream.
        _lockStream?.Dispose();
        _lockStream = null;
    }

    /// <returns>True if the listener started; false with <paramref name="error"/> set otherwise. Never throws — every failure mode is caught and reported so <see cref="TryAcquire"/> can release the lock cleanly instead of propagating an unhandled exception out of a "did this succeed" call.</returns>
    private bool StartActivationListener(out string? error)
    {
        error = null;

        // A stale socket file can be left behind after an unclean shutdown (e.g. SIGKILL); Bind
        // fails with AddressInUse against a leftover path even though nothing is listening.
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }

        Socket listenerSocket;
        try
        {
            listenerSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listenerSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
            listenerSocket.Listen();
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            error = $"Could not start the activation listener at '{_socketPath}': {ex.Message}";
            return false;
        }

        _listenerSocket = listenerSocket;
        _listenerCts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => AcceptLoopAsync(listenerSocket, _listenerCts.Token));
        return true;
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

    /// <summary>
    /// Best-effort chmod to owner-only, then reads the mode back to confirm it actually landed —
    /// a chmod against a directory this process doesn't own (e.g. pre-created by another user on a
    /// shared /tmp fallback) silently fails rather than throwing in some environments, so the only
    /// reliable signal is the post-condition, not whether the call itself threw.
    /// </summary>
    private static bool EnsureOwnerOnlyPermissions(string directory)
    {
        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        try
        {
            File.SetUnixFileMode(directory, ownerOnly);
        }
        catch (IOException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        try
        {
            return File.GetUnixFileMode(directory) == ownerOnly;
        }
        catch (IOException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }
}
