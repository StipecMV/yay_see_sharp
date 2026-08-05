using System;
using System.IO;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Platform;

public class FileLockSingleInstanceServiceTests
{
    [Test]
    public async Task First_instance_acquires_lock_and_second_is_blocked()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lock");
        var first = new FileLockSingleInstanceService(path);
        var second = new FileLockSingleInstanceService(path);

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
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".lock");
        var first = new FileLockSingleInstanceService(path);
        var second = new FileLockSingleInstanceService(path);

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
}
