using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;

namespace yay_see_sharp.integration.Tests;

/// <summary>
/// Destructive: actually installs then removes a real package via the system's yay binary. Gated
/// the same way as the existing Arch integration test — set
/// YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1 on an Arch/CachyOS host with yay installed. Skips
/// automatically everywhere else.
/// </summary>
[Category("Integration")]
public class PackageUninstallIntegrationTests
{
    private const string TestPackage = "hello";

    [Test]
    public async Task Yay_uninstall_of_a_real_package_exits_zero_and_removes_it()
    {
        IntegrationSkip.ThrowIfArchGateNotSet();

        var runner = new SystemCommandRunner();

        // Setup: guarantee the package is actually installed before we try to remove it.
        var installResult = await runner.RunAsync(new CommandRequest("yay", ["--needed", "--noconfirm", "-S", TestPackage]));
        await Assert.That(installResult.ExitCode).IsEqualTo(0);

        var uninstallResult = await runner.RunAsync(new CommandRequest("yay", ["-Rns", "--noconfirm", TestPackage]));

        await Assert.That(uninstallResult.ExitCode).IsEqualTo(0);

        var backend = new YayPackageBackend(runner, new YayOutputParser());
        var details = await backend.GetDetailsAsync(TestPackage);

        await Assert.That(details).IsNull();
    }
}
