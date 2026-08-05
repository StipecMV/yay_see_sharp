using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;

namespace yay_see_sharp.integration.Tests;

/// <summary>
/// End-to-end install-then-uninstall through the full YayPackageBackend (search, install, verify,
/// uninstall, verify) on a real Arch/CachyOS host. Gated the same way as the other Arch-only
/// tests in this project — set YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1. Skips automatically
/// everywhere else.
/// </summary>
[Category("Integration")]
public class ArchIntegrationTests
{
    private const string TestPackage = "hello";

    [Test]
    public async Task Yay_install_then_uninstall_hello_on_real_arch_host()
    {
        IntegrationSkip.ThrowIfArchGateNotSet();

        var backend = new YayPackageBackend(new SystemCommandRunner(), new YayOutputParser());

        var searchResults = await backend.SearchAsync(TestPackage);
        await Assert.That(searchResults.Any(package => package.Name == TestPackage)).IsTrue();

        PackageOperationProgress? lastInstallProgress = null;
        await foreach (var progress in backend.InstallAsync(TestPackage))
        {
            lastInstallProgress = progress;
        }
        await Assert.That(lastInstallProgress?.Stage).IsEqualTo(PackageOperationStage.Completed);

        var installedDetails = await backend.GetDetailsAsync(TestPackage);
        await Assert.That(installedDetails).IsNotNull();
        await Assert.That(installedDetails!.Summary.State).IsEqualTo(PackageState.Installed);

        PackageOperationProgress? lastUninstallProgress = null;
        await foreach (var progress in backend.UninstallAsync(TestPackage, removeOrphans: true))
        {
            lastUninstallProgress = progress;
        }
        await Assert.That(lastUninstallProgress?.Stage).IsEqualTo(PackageOperationStage.Completed);

        var removedDetails = await backend.GetDetailsAsync(TestPackage);
        await Assert.That(removedDetails).IsNull();
    }
}
