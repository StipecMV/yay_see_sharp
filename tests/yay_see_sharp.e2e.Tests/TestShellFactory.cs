using yay_see_sharp.application.ViewModels;
using yay_see_sharp.application.Views;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Filesystem;
using yay_see_sharp.infrastructure.Http;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Platform;
using yay_see_sharp.infrastructure.Settings;

namespace yay_see_sharp.e2e.Tests;

/// <summary>
/// Builds the same MainWindow/MainWindowViewModel graph AppBootstrapper wires up in production,
/// minus the real infrastructure side effects (sudo, notify-send, single-instance file lock) that
/// have no place in a headless test run. Settings persist to a throwaway per-call temp file so
/// tests never read or write the developer's real settings.json.
/// </summary>
public static class TestShellFactory
{
    public static (MainWindow Window, MainWindowViewModel ViewModel, SettingsViewModel Settings) Create(
        IPackageBackend? backend = null,
        ILocalizationService? localization = null)
    {
        var actualBackend = backend ?? new DemoPackageBackend();
        var actualLocalization = localization ?? new LocalizationService("en");
        var settingsStore = new FileSettingsStore(
            Path.Combine(Path.GetTempPath(), $"yay-see-sharp-e2e-{Guid.NewGuid():N}.json"));
        var settings = new SettingsViewModel(
            settingsStore, actualLocalization, AppSettings.Default, new EngineDetector(), new FolderBrowserService());
        var pkgbuildService = new PkgbuildService();
        var dashboard = new DashboardViewModel(actualBackend, actualLocalization);
        var search = new SearchViewModel(actualBackend, actualLocalization, pkgbuildService, settings);
        var installed = new InstalledPackagesViewModel(actualBackend, actualLocalization, pkgbuildService, settings);
        var viewModel = new MainWindowViewModel(
            actualBackend, actualLocalization, settings, dashboard, search, installed);

        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        return (window, viewModel, settings);
    }
}
