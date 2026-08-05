using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Settings;
using yay_see_sharp.application.ViewModels;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel CreateViewModel(
        yay_see_sharp.domain.Abstractions.IPackageBackend backend,
        LocalizationService? localization = null)
    {
        var localizationService = localization ?? new LocalizationService("en");
        var settings = new SettingsViewModel(new FileSettingsStore(), localizationService, AppSettings.Default);
        return new MainWindowViewModel(
            backend,
            localizationService,
            settings,
            new DashboardViewModel(backend, localizationService),
            new SearchViewModel(backend, localizationService, settings),
            new InstalledPackagesViewModel(backend, localizationService, settings));
    }

    [Test]
    public async Task Shell_exposes_backend_distribution_and_mode()
    {
        var backend = new DemoPackageBackend(new BackendInfo("ubuntu", "Ubuntu Demo", "demo", BackendMode.Demo, false, "Demo warning"));
        var viewModel = CreateViewModel(backend);

        await Assert.That(viewModel.DistributionName).IsEqualTo("Ubuntu Demo");
        await Assert.That(viewModel.PackageManager).IsEqualTo("demo");
        await Assert.That(viewModel.ModeLabel).IsEqualTo("Demo");
        await Assert.That(viewModel.HasWarning).IsTrue();
        await Assert.That(viewModel.Theme).IsEqualTo(ThemePreference.System);
    }

    [Test]
    public async Task Shell_defaults_to_dashboard_page_and_switches_on_navigation()
    {
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend);

        await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Dashboard);

        var searchItem = viewModel.NavigationItems[1];
        viewModel.SelectedNavigationItem = searchItem;

        await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Search);
    }

    [Test]
    public async Task Shell_switches_to_installed_page_on_navigation()
    {
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend);

        viewModel.SelectedNavigationItem = viewModel.NavigationItems[2];

        await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Installed);
    }

    [Test]
    public async Task Shell_switches_to_settings_page_on_navigation()
    {
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend);

        viewModel.SelectedNavigationItem = viewModel.NavigationItems[3];

        await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Settings);
    }

    [Test]
    public async Task Switching_language_live_updates_navigation_titles_and_mode_label()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend, localization);

        await Assert.That(viewModel.NavigationItems[0].Title).IsEqualTo("Dashboard");
        await Assert.That(viewModel.ModeLabel).IsEqualTo("Demo");
        await Assert.That(viewModel.ThemeLabel).IsEqualTo("System");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.NavigationItems[0].Title).IsEqualTo("Prehľad");
        await Assert.That(viewModel.NavigationItems[1].Title).IsEqualTo("Hľadať");
        await Assert.That(viewModel.NavigationItems[2].Title).IsEqualTo("Nainštalované");
        await Assert.That(viewModel.NavigationItems[3].Title).IsEqualTo("Nastavenia");
        await Assert.That(viewModel.ModeLabel).IsEqualTo("Demo");
        await Assert.That(viewModel.ThemeLabel).IsEqualTo("Systémová");
    }

    [Test]
    public async Task Switching_language_live_updates_unsupported_warning_message()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend(new BackendInfo("ubuntu", "Ubuntu Demo", "demo", BackendMode.Demo, false));
        var viewModel = CreateViewModel(backend, localization);

        await Assert.That(viewModel.WarningMessage).Contains("Ubuntu Demo");
        await Assert.That(viewModel.WarningMessage).Contains("Real yay backend");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.WarningMessage).Contains("Ubuntu Demo");
        await Assert.That(viewModel.WarningMessage).Contains("Reálny yay backend");
    }

    [Test]
    public async Task Switching_language_once_propagates_live_across_the_whole_shell_tree()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend, localization);

        await Assert.That(viewModel.NavigationItems[0].Title).IsEqualTo("Dashboard");
        await Assert.That(viewModel.Dashboard.RefreshLabel).IsEqualTo("Refresh");
        await Assert.That(viewModel.Search.SearchButtonLabel).IsEqualTo("Search");
        await Assert.That(viewModel.Settings.SavedLabel).IsEqualTo("Saved");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.NavigationItems[0].Title).IsEqualTo("Prehľad");
        await Assert.That(viewModel.Dashboard.RefreshLabel).IsEqualTo("Obnoviť");
        await Assert.That(viewModel.Search.SearchButtonLabel).IsEqualTo("Hľadať");
        await Assert.That(viewModel.Settings.SavedLabel).IsEqualTo("Uložené");

        viewModel.Search.Query = "hello";
        await viewModel.Search.SearchCommand.Execute();
        viewModel.Search.SelectedPackage = viewModel.Search.Results[0];

        await Assert.That(viewModel.Search.SelectedDetails!.InstallLabel).IsEqualTo("Inštalovať");
    }

    [Test]
    public async Task Package_manager_is_hidden_in_demo_mode_to_avoid_duplicate_demo_label()
    {
        var backend = new DemoPackageBackend(new BackendInfo("ubuntu", "Ubuntu Demo", "demo", BackendMode.Demo, false));
        var viewModel = CreateViewModel(backend);

        await Assert.That(viewModel.PackageManager).IsEqualTo("demo");
        await Assert.That(viewModel.ModeLabel).IsEqualTo("Demo");
        await Assert.That(viewModel.ShowPackageManager).IsFalse();
    }

    [Test]
    public async Task Package_manager_is_shown_in_real_mode()
    {
        var backend = new DemoPackageBackend(new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true));
        var viewModel = CreateViewModel(backend);

        await Assert.That(viewModel.ShowPackageManager).IsTrue();
    }

    [Test]
    public async Task Changing_settings_theme_updates_shell_theme_live()
    {
        var backend = new DemoPackageBackend();
        var viewModel = CreateViewModel(backend);

        await Assert.That(viewModel.Theme).IsEqualTo(ThemePreference.System);
        await Assert.That(viewModel.ThemeLabel).IsEqualTo("System");

        viewModel.Settings.Theme = ThemePreference.Dark;

        await Assert.That(viewModel.Theme).IsEqualTo(ThemePreference.Dark);
        await Assert.That(viewModel.ThemeLabel).IsEqualTo("Dark");
    }
}
