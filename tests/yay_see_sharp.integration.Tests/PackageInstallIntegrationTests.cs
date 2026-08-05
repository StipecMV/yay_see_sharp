using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;

namespace yay_see_sharp.integration.Tests;

/// <summary>
/// Destructive: actually installs a real package via the system's yay binary. Gated the same way
/// as the existing Arch integration test — set YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1 on an
/// Arch/CachyOS host with yay installed. Skips automatically everywhere else.
/// </summary>
[Category("Integration")]
public class PackageInstallIntegrationTests
{
    private const string TestPackage = "hello";

    [Test]
    public async Task Yay_install_of_a_real_package_exits_zero_and_marks_it_installed()
    {
        IntegrationSkip.ThrowIfArchGateNotSet();

        var runner = new SystemCommandRunner();
        try
        {
            var result = await runner.RunAsync(new CommandRequest("yay", ["--needed", "--noconfirm", "-S", TestPackage]));

            await Assert.That(result.ExitCode).IsEqualTo(0);

            var backend = new YayPackageBackend(runner, new YayOutputParser());
            var details = await backend.GetDetailsAsync(TestPackage);

            await Assert.That(details).IsNotNull();
            await Assert.That(details!.Summary.State).IsEqualTo(PackageState.Installed);
        }
        finally
        {
            // Leave the host as we found it, regardless of how the assertions above turned out.
            await runner.RunAsync(new CommandRequest("yay", ["-Rns", "--noconfirm", TestPackage]));
        }
    }
}
