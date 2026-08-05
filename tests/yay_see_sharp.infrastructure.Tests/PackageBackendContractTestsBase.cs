using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

/// <summary>Package names a concrete contract test fixture wires up so the shared assertions below apply equally to DemoPackageBackend and a faked YayPackageBackend.</summary>
public sealed record ContractPackages(
    string NotInstalled,
    string Installed,
    string Updatable,
    string Invalid,
    string Failing);

/// <summary>
/// Runs the same behavioral assertions against any IPackageBackend implementation. Concrete
/// subclasses supply the backend and its package names; TUnit discovers and runs the inherited
/// [Test] methods once per subclass, so this verifies DemoPackageBackend and YayPackageBackend
/// (backed by FakeYayCommandRunner) honor an identical contract.
/// </summary>
public abstract class PackageBackendContractTestsBase
{
    protected abstract IPackageBackend CreateBackend(string? failingPackageName = null);

    protected abstract ContractPackages Packages { get; }

    [Test]
    public async Task Install_of_a_not_installed_package_completes_successfully()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.InstallAsync(Packages.NotInstalled));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Install);
    }

    [Test]
    public async Task Install_of_a_package_that_fails_reports_failed()
    {
        var backend = CreateBackend(failingPackageName: Packages.Failing);

        var progress = await CollectAsync(backend.InstallAsync(Packages.Failing));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Install);
    }

    [Test]
    public async Task Install_of_an_invalid_package_name_reports_failed()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.InstallAsync(Packages.Invalid));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Install);
    }

    [Test]
    public async Task Install_that_is_cancelled_reports_cancelled()
    {
        var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = await CollectAsync(backend.InstallAsync(Packages.NotInstalled, cts.Token));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Install);
    }

    [Test]
    public async Task Uninstall_with_remove_orphans_true_completes_successfully()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.UninstallAsync(Packages.Installed, removeOrphans: true));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Uninstall);
    }

    [Test]
    public async Task Uninstall_with_remove_orphans_false_completes_successfully()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.UninstallAsync(Packages.Installed, removeOrphans: false));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Uninstall);
    }

    [Test]
    public async Task Uninstall_that_is_cancelled_reports_cancelled()
    {
        var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = await CollectAsync(backend.UninstallAsync(Packages.Installed, removeOrphans: true, cts.Token));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Uninstall);
    }

    [Test]
    public async Task Update_of_all_packages_completes_successfully()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.UpdateAsync([]));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Update);
    }

    [Test]
    public async Task Update_of_selected_packages_completes_successfully()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.UpdateAsync([Packages.Updatable]));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Update);
    }

    [Test]
    public async Task Update_naming_an_invalid_package_reports_failed()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.UpdateAsync([Packages.Invalid]));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Update);
    }

    [Test]
    public async Task Update_that_is_cancelled_reports_cancelled()
    {
        var backend = CreateBackend();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var progress = await CollectAsync(backend.UpdateAsync([], cts.Token));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Update);
    }

    [Test]
    public async Task Completed_operations_preserve_output_text()
    {
        var backend = CreateBackend();

        var progress = await CollectAsync(backend.InstallAsync(Packages.NotInstalled));

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Output).IsNotNull();
        await Assert.That(progress[^1].Output).IsNotEmpty();
    }

    private static async Task<List<PackageOperationProgress>> CollectAsync(
        IAsyncEnumerable<PackageOperationProgress> source)
    {
        var results = new List<PackageOperationProgress>();
        await foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }
}
