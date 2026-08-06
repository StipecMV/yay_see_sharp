using System;
using System.IO;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.infrastructure.Tests;

public class FileLockSingleInstanceServiceTests
{
    private static string CreateTestDirectory() =>
        Path.Combine(Path.GetTempPath(), "yay_see_sharp_test_" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task First_instance_acquires_lock_and_second_is_blocked()
    {
        var dir = CreateTestDirectory();
        var first = new FileLockSingleInstanceService(dir);
        var second = new FileLockSingleInstanceService(dir);

        try
        {
            await Assert.That(first.TryAcquire()).IsTrue();
            await Assert.That(second.TryAcquire()).IsFalse();
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Test]
    public async Task Lock_becomes_available_after_disposing_the_holder()
    {
        var dir = CreateTestDirectory();
        var first = new FileLockSingleInstanceService(dir);
        var second = new FileLockSingleInstanceService(dir);

        await Assert.That(first.TryAcquire()).IsTrue();
        first.Dispose();

        try
        {
            await Assert.That(second.TryAcquire()).IsTrue();
        }
        finally
        {
            second.Dispose();
        }
    }

    [Test]
    public async Task Dispose_does_not_delete_the_lock_file()
    {
        var dir = CreateTestDirectory();
        var service = new FileLockSingleInstanceService(dir);

        await Assert.That(service.TryAcquire()).IsTrue();
        service.Dispose();

        await Assert.That(File.Exists(Path.Combine(dir, "instance.lock"))).IsTrue();
    }

    [Test]
    public async Task Runtime_directory_is_created_with_owner_only_permissions_on_linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new TUnit.Core.Exceptions.SkipTestException("Unix file mode is only meaningful on Linux.");
        }

        var dir = CreateTestDirectory();
        var service = new FileLockSingleInstanceService(dir);
        try
        {
            var mode = File.GetUnixFileMode(dir);
            await Assert.That(mode).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Test]
    public async Task Second_instance_activation_request_is_received_by_the_first()
    {
        var dir = CreateTestDirectory();
        var first = new FileLockSingleInstanceService(dir);
        var second = new FileLockSingleInstanceService(dir);
        var received = new TaskCompletionSource();

        try
        {
            await Assert.That(first.TryAcquire()).IsTrue();
            first.ActivationRequested += (_, _) => received.TrySetResult();

            await Assert.That(second.TryAcquire()).IsFalse();
            await Assert.That(second.TryActivateExisting()).IsTrue();

            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    [Test]
    public async Task Activating_with_no_running_instance_returns_false()
    {
        var dir = CreateTestDirectory();
        var lonely = new FileLockSingleInstanceService(dir);

        try
        {
            await Assert.That(lonely.TryActivateExisting()).IsFalse();
        }
        finally
        {
            lonely.Dispose();
        }
    }

    [Test]
    public async Task Listener_stops_and_disposing_is_safe_after_app_exit()
    {
        var dir = CreateTestDirectory();
        var service = new FileLockSingleInstanceService(dir);
        await Assert.That(service.TryAcquire()).IsTrue();

        service.Dispose();

        // A second attempt to activate after the listener has shut down must not hang or throw.
        var other = new FileLockSingleInstanceService(dir);
        try
        {
            await Assert.That(other.TryActivateExisting()).IsFalse();
        }
        finally
        {
            other.Dispose();
        }
    }

    [Test]
    public async Task Bind_failure_after_lock_acquired_releases_the_lock_and_reports_a_reason()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new TUnit.Core.Exceptions.SkipTestException("Unix domain sockets are only meaningful on Linux.");
        }

        // Unix domain socket paths are capped at ~108 bytes on Linux (sun_path) — a directory this
        // deep pushes activate.sock past that limit, so Socket.Bind fails deterministically without
        // needing to fake permissions or race another process.
        var dir = Path.Combine(CreateTestDirectory(), new string('a', 120));
        var service = new FileLockSingleInstanceService(dir);

        var acquired = service.TryAcquire();

        await Assert.That(acquired).IsFalse();
        await Assert.That(service.LastFailureReason).IsNotNull();
        service.Dispose();

        // FINDING-06/NEW-03: a listener failure must release the lock, not hold it silently. A
        // second attempt against the *same* directory (still with the too-long socket path, so its
        // own listener will fail too) proves this only by both failing for the *listener* reason
        // rather than the first one still holding the file lock — assert via a lock-only probe: try
        // opening the lock file exclusively ourselves, bypassing the service entirely.
        using var probe = new FileStream(
            Path.Combine(dir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        probe.Dispose();
    }

    [Test]
    public async Task New_instance_listener_is_reachable_immediately_after_previous_holder_disposes()
    {
        var dir = CreateTestDirectory();
        var first = new FileLockSingleInstanceService(dir);
        await Assert.That(first.TryAcquire()).IsTrue();
        first.Dispose();

        // NEW-02: socket cleanup happens before lock release in Dispose, so there is no window
        // where a fresh instance's just-bound socket could be deleted by the old instance's
        // (already-completed) cleanup.
        var second = new FileLockSingleInstanceService(dir);
        var received = new TaskCompletionSource();
        try
        {
            await Assert.That(second.TryAcquire()).IsTrue();
            second.ActivationRequested += (_, _) => received.TrySetResult();

            var third = new FileLockSingleInstanceService(dir);
            try
            {
                await Assert.That(third.TryAcquire()).IsFalse();
                await Assert.That(third.TryActivateExisting()).IsTrue();
                await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                third.Dispose();
            }
        }
        finally
        {
            second.Dispose();
        }
    }

    [Test]
    public async Task Repeated_acquire_dispose_cycles_from_alternating_instances_never_let_two_hold_the_lock_at_once()
    {
        var dir = CreateTestDirectory();

        for (var i = 0; i < 10; i++)
        {
            var holder = new FileLockSingleInstanceService(dir);
            var contender = new FileLockSingleInstanceService(dir);
            try
            {
                await Assert.That(holder.TryAcquire()).IsTrue();
                await Assert.That(contender.TryAcquire()).IsFalse();
                await Assert.That(contender.LastFailureReason).IsNull();
            }
            finally
            {
                holder.Dispose();
                contender.Dispose();
            }
        }
    }
}
