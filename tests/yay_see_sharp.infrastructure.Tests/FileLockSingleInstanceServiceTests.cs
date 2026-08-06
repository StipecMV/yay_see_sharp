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
}
