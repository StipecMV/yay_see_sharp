using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class InstalledPackagesViewModelTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within the timeout.");
        }
    }

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
    public async Task Filtered_packages_mirrors_all_packages_by_default()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());

        await Assert.That(viewModel.FilteredPackages.Count).IsEqualTo(viewModel.Packages.Count);
        await Assert.That(viewModel.HasNoFilteredResults).IsFalse();
    }

    [Test]
    public async Task Source_filter_narrows_filtered_packages_to_the_selected_source()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        await Assert.That(viewModel.Packages.Any(p => p.Source == PackageSource.Aur)).IsTrue();
        await Assert.That(viewModel.Packages.Any(p => p.Source == PackageSource.Official)).IsTrue();

        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);

        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count > 0 && viewModel.FilteredPackages.All(p => p.Source == PackageSource.Aur),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Query_narrows_filtered_packages_by_name()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        var target = viewModel.Packages.First();

        viewModel.Query = target.Name;

        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count > 0 && viewModel.FilteredPackages.All(p => p.Name == target.Name),
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task A_query_matching_nothing_reports_no_filtered_results_but_not_empty()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());

        viewModel.Query = "this-package-does-not-exist-anywhere";

        await WaitUntilAsync(() => viewModel.FilteredPackages.Count == 0, TimeSpan.FromSeconds(2));
        await Assert.That(viewModel.HasNoFilteredResults).IsTrue();
        await Assert.That(viewModel.HasNoPackages).IsFalse();
    }

    // BUGFIX-2026-08: clearing the search box must bring the full installed list back —
    // the filter no longer gets stuck on a previously selected Official/AUR source.
    [Test]
    public async Task Clearing_the_query_restores_all_packages()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        var target = viewModel.Packages.First();

        viewModel.Query = target.Name;
        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count > 0 && viewModel.FilteredPackages.All(p => p.Name == target.Name),
            TimeSpan.FromSeconds(2));

        viewModel.Query = string.Empty;

        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count == viewModel.Packages.Count,
            TimeSpan.FromSeconds(2));
    }

    // BUGFIX-2026-08: switching All → Official → AUR applies immediately (no keystroke needed),
    // and coming back to All shows everything again.
    [Test]
    public async Task Switching_the_source_filter_then_back_to_all_restores_everything()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        await Assert.That(viewModel.Packages.Any(p => p.Source == PackageSource.Official)).IsTrue();

        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Official);
        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count > 0 && viewModel.FilteredPackages.All(p => p.Source == PackageSource.Official),
            TimeSpan.FromSeconds(2));

        viewModel.SelectedSourceOption = viewModel.SourceOptions[0]; // All
        await WaitUntilAsync(
            () => viewModel.FilteredPackages.Count == viewModel.Packages.Count,
            TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Selecting_by_name_resets_an_active_filter_that_would_otherwise_hide_the_target()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new InstalledPackagesViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());
        var official = viewModel.Packages.First(p => p.Source == PackageSource.Official);
        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);
        await WaitUntilAsync(() => !viewModel.FilteredPackages.Contains(official), TimeSpan.FromSeconds(2));

        viewModel.SelectByName(official.Name);

        await Assert.That(viewModel.SelectedPackage).IsEqualTo(official);
        await Assert.That(viewModel.FilteredPackages.Contains(official)).IsTrue();
        await Assert.That(viewModel.SourceFilter).IsNull();
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
