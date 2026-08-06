using Moq;
using yay_see_sharp.domain.Abstractions;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class InstalledPackagesViewModelTests
{
    [Test]
    public async Task Constructor_loads_installed_packages_automatically()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());

        await Assert.That(viewModel.Packages.Count).IsGreaterThan(0);
        await Assert.That(viewModel.HasNoPackages).IsFalse();
        await Assert.That(viewModel.Packages.Any(package => package.Name == "firefox")).IsTrue();
        await Assert.That(viewModel.Packages.Any(package => package.Name == "hello")).IsFalse();
    }

    [Test]
    public async Task Selecting_a_package_creates_details_for_it()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());

        viewModel.SelectedPackage = viewModel.Packages[0];

        await Assert.That(viewModel.SelectedDetails).IsNotNull();
        await Assert.That(viewModel.SelectedDetails!.Summary).IsEqualTo(viewModel.Packages[0]);
    }

    [Test]
    public async Task Uninstalling_from_the_installed_tab_updates_details_state()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        var firefox = viewModel.Packages.Single(package => package.Name == "firefox");
        viewModel.SelectedPackage = firefox;
        await viewModel.SelectedDetails!.LoadAsync();

        await viewModel.SelectedDetails!.UninstallCommand.Execute();

        await Assert.That(viewModel.SelectedDetails!.Details!.Summary.State).IsEqualTo(yay_see_sharp.domain.Models.PackageState.NotInstalled);
    }

    [Test]
    public async Task Switching_language_live_updates_labels()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, localization, Mock.Of<IPkgbuildService>());

        await Assert.That(viewModel.RefreshLabel).IsEqualTo("Refresh");
        await Assert.That(viewModel.EmptyLabel).IsEqualTo("No packages installed.");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.RefreshLabel).IsEqualTo("Obnoviť");
        await Assert.That(viewModel.EmptyLabel).IsEqualTo("Žiadne nainštalované balíky.");
    }
}
