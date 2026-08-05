using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

public class PackageDetailsViewModelTests
{
    [Test]
    public async Task Load_fetches_details_for_selected_package()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("firefox", "1.0", "browser", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));

        await viewModel.LoadAsync();

        await Assert.That(viewModel.Details).IsNotNull();
        await Assert.That(viewModel.IsBusy).IsFalse();
    }

    [Test]
    public async Task Install_completes_successfully_and_reloads_installed_state()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));

        await viewModel.InstallCommand.Execute();

        await Assert.That(viewModel.Details!.Summary.State).IsEqualTo(PackageState.Installed);
    }

    [Test]
    public async Task Operation_is_cleared_after_install_completes_so_progress_ui_disappears()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));

        var stagesSeen = new List<PackageOperationStage>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PackageDetailsViewModel.Operation) && viewModel.Operation is not null)
            {
                stagesSeen.Add(viewModel.Operation.Stage);
            }
        };

        await viewModel.InstallCommand.Execute();

        await Assert.That(viewModel.Operation).IsNull();
        await Assert.That(stagesSeen).IsNotEmpty();
    }

    [Test]
    public async Task Operation_is_cleared_after_uninstall_completes_so_progress_ui_disappears()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));
        await viewModel.InstallCommand.Execute();

        await viewModel.UninstallCommand.Execute();

        await Assert.That(viewModel.Operation).IsNull();
        await Assert.That(viewModel.Details!.Summary.State).IsEqualTo(PackageState.NotInstalled);
    }

    [Test]
    public async Task Switching_language_live_updates_install_and_uninstall_labels()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("firefox", "1.0", "browser", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, localization);

        await Assert.That(viewModel.InstallLabel).IsEqualTo("Install");
        await Assert.That(viewModel.UninstallLabel).IsEqualTo("Uninstall");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.InstallLabel).IsEqualTo("Inštalovať");
        await Assert.That(viewModel.UninstallLabel).IsEqualTo("Odinštalovať");
    }

    [Test]
    public async Task Uninstall_is_disabled_and_state_label_reflects_not_installed_for_a_fresh_package()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));
        await viewModel.LoadAsync();

        await Assert.That(((ICommand)viewModel.UninstallCommand).CanExecute(null)).IsFalse();
        await Assert.That(((ICommand)viewModel.InstallCommand).CanExecute(null)).IsTrue();
        await Assert.That(viewModel.StateLabel).IsEqualTo("Not installed");
    }

    [Test]
    public async Task Install_is_disabled_and_state_label_reflects_installed_after_installing()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));

        await viewModel.InstallCommand.Execute();

        await Assert.That(((ICommand)viewModel.InstallCommand).CanExecute(null)).IsFalse();
        await Assert.That(((ICommand)viewModel.UninstallCommand).CanExecute(null)).IsTrue();
        await Assert.That(viewModel.StateLabel).IsEqualTo("Installed");
    }

    [Test]
    public async Task Uninstalling_an_already_uninstalled_package_is_rejected_by_can_execute()
    {
        var backend = new DemoPackageBackend();
        var summary = new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled);
        var viewModel = new PackageDetailsViewModel(backend, summary, new LocalizationService("en"));
        await viewModel.LoadAsync();

        await Assert.That(await viewModel.UninstallCommand.CanExecute.FirstAsync()).IsFalse();
    }

    [Test]
    public async Task Uninstall_passes_remove_orphans_true_through_to_the_backend_when_the_policy_says_so()
    {
        var summary = new PackageSummary("firefox", "1.0", "browser", PackageSource.Official, 0, PackageState.Installed);
        var details = new PackageDetails(summary, null, null, [], []);
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.GetDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(details);
        bool? capturedRemoveOrphans = null;
        backend.Setup(b => b.UninstallAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((string _, bool removeOrphans, CancellationToken _) =>
            {
                capturedRemoveOrphans = removeOrphans;
                return CompletedProgress();
            });

        var viewModel = new PackageDetailsViewModel(
            backend.Object, summary, new LocalizationService("en"), uninstallPolicy: new FakeUninstallPolicy(true));
        await viewModel.LoadAsync();

        await viewModel.UninstallCommand.Execute();

        await Assert.That(capturedRemoveOrphans).IsTrue();
    }

    [Test]
    public async Task Uninstall_passes_remove_orphans_false_through_to_the_backend_when_the_policy_says_so()
    {
        var summary = new PackageSummary("firefox", "1.0", "browser", PackageSource.Official, 0, PackageState.Installed);
        var details = new PackageDetails(summary, null, null, [], []);
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.GetDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(details);
        bool? capturedRemoveOrphans = null;
        backend.Setup(b => b.UninstallAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((string _, bool removeOrphans, CancellationToken _) =>
            {
                capturedRemoveOrphans = removeOrphans;
                return CompletedProgress();
            });

        var viewModel = new PackageDetailsViewModel(
            backend.Object, summary, new LocalizationService("en"), uninstallPolicy: new FakeUninstallPolicy(false));
        await viewModel.LoadAsync();

        await viewModel.UninstallCommand.Execute();

        await Assert.That(capturedRemoveOrphans).IsFalse();
    }

    private static async IAsyncEnumerable<PackageOperationProgress> CompletedProgress()
    {
        await Task.Yield();
        yield return new PackageOperationProgress(PackageOperationKind.Uninstall, PackageOperationStage.Completed, 100, "done");
    }

    private sealed class FakeUninstallPolicy(bool removeOrphansByDefault) : IUninstallPolicy
    {
        public bool RemoveOrphansByDefault => removeOrphansByDefault;
    }
}
