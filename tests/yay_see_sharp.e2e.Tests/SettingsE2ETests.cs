using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ReactiveUI;
using yay_see_sharp.application.Platform;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.application.Views;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Http;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Platform;
using yay_see_sharp.infrastructure.Settings;

namespace yay_see_sharp.e2e.Tests;

public class SettingsE2ETests
{
    [Test]
    public async Task Changing_the_language_setting_updates_rendered_combobox_labels_in_place()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, settings) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;

            viewModel.SelectedNavigationItem = viewModel.NavigationItems[3];
            AvaloniaUiTest.Pump();

            var comboBox = window.GetVisualDescendants().OfType<ComboBox>()
                .First(box => ReferenceEquals(box.ItemsSource, settings.LanguageOptions));
            var englishOption = settings.LanguageOptions.Single(o => o.Value == "en");
            var slovakOption = settings.LanguageOptions.Single(o => o.Value == "sk");

            await Assert.That(englishOption.Label).IsEqualTo("English");
            await Assert.That(slovakOption.Label).IsEqualTo("Slovak");

            settings.Language = "sk";
            AvaloniaUiTest.Pump();

            // The ComboBox's ItemsSource must still be the exact same list instance: rebuilding
            // it on language change (instead of mutating each SelectableOption.Label in place) is
            // what used to desync SelectedValue and empty the ComboBox — see
            // SettingsViewModel.RaiseLocalizedPropertiesChanged.
            await Assert.That(ReferenceEquals(comboBox.ItemsSource, settings.LanguageOptions)).IsTrue();
            await Assert.That(englishOption.Label).IsEqualTo("Angličtina");
            await Assert.That(slovakOption.Label).IsEqualTo("Slovenčina");
        });
    }

    [Test]
    public async Task Changing_the_theme_setting_switches_the_application_theme_variant()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, settings) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;

            // Mirrors the theme -> RequestedThemeVariant wiring AppBootstrapper/App.axaml.cs sets
            // up in production (that wiring lives at the App level, which headless tests
            // deliberately don't boot — see TestAppBuilder — so it's reproduced here to verify
            // real Avalonia theme switching behaves as expected when driven by the setting).
            using var subscription = settings.WhenAnyValue(x => x.Theme).Subscribe(theme =>
            {
                Application.Current!.RequestedThemeVariant = theme switch
                {
                    ThemePreference.Light => ThemeVariant.Light,
                    ThemePreference.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default,
                };
            });

            settings.Theme = ThemePreference.Dark;
            AvaloniaUiTest.Pump();
            await Assert.That(Application.Current!.RequestedThemeVariant).IsEqualTo(ThemeVariant.Dark);
            await Assert.That(window.ActualThemeVariant).IsEqualTo(ThemeVariant.Dark);

            settings.Theme = ThemePreference.Light;
            AvaloniaUiTest.Pump();
            await Assert.That(Application.Current!.RequestedThemeVariant).IsEqualTo(ThemeVariant.Light);
            await Assert.That(window.ActualThemeVariant).IsEqualTo(ThemeVariant.Light);
        });
    }

    // BUGFIX-2026-08: regression — opening Settings with zero changes used to surface a "Saved"
    // toast (a load-time binding push wrote settings back and the auto-save toasted). The view
    // must load silently: no settings property may change, nothing may be saved, no toast.
    [Test]
    public async Task Opening_settings_without_changes_shows_no_saved_toast()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var toastService = new ToastService();
            var localization = new LocalizationService("en");
            var settingsStore = new FileSettingsStore(
                Path.Combine(Path.GetTempPath(), $"yay-see-sharp-toast-{Guid.NewGuid():N}.json"));
            var settings = new SettingsViewModel(settingsStore, localization, AppSettings.Default, new EngineDetector(), toastService);
            var pkgbuildService = new PkgbuildService();
            var backend = new DemoPackageBackend();
            var dashboard = new DashboardViewModel(backend, localization);
            var search = new SearchViewModel(backend, localization, pkgbuildService, settings, toastService);
            var installed = new InstalledPackagesViewModel(backend, localization, pkgbuildService, settings, toastService);
            var viewModel = new MainWindowViewModel(
                backend, localization, settings, dashboard, search, installed, toastService: toastService);
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            // Capture any settings property written while the Settings view loads.
            var changedProperties = new List<string?>();
            settings.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            viewModel.SelectedNavigationItem = viewModel.NavigationItems[3]; // Settings
            AvaloniaUiTest.Pump();

            // Let any debounced/async save settle (settings writes are async file I/O).
            await Task.Delay(500);
            AvaloniaUiTest.Pump();

            await Assert.That(changedProperties).IsEmpty();
            await Assert.That(toastService.Toasts).IsEmpty();
            await Assert.That(settings.IsSaved).IsFalse();
        });
    }

    // BUGFIX-2026-08: same class of regression for a non-default theme — the Theme segmented
    // ListBox must not push "System" (its first item) back into the settings when the view loads.
    [Test]
    public async Task Opening_settings_preserves_a_non_default_theme_without_saving()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var toastService = new ToastService();
            var localization = new LocalizationService("en");
            var settingsStore = new FileSettingsStore(
                Path.Combine(Path.GetTempPath(), $"yay-see-sharp-theme-{Guid.NewGuid():N}.json"));
            var initial = AppSettings.Default with { Theme = ThemePreference.Dark };
            var settings = new SettingsViewModel(settingsStore, localization, initial, new EngineDetector(), toastService);
            var pkgbuildService = new PkgbuildService();
            var backend = new DemoPackageBackend();
            var dashboard = new DashboardViewModel(backend, localization);
            var search = new SearchViewModel(backend, localization, pkgbuildService, settings, toastService);
            var installed = new InstalledPackagesViewModel(backend, localization, pkgbuildService, settings, toastService);
            var viewModel = new MainWindowViewModel(
                backend, localization, settings, dashboard, search, installed, toastService: toastService);
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            viewModel.SelectedNavigationItem = viewModel.NavigationItems[3]; // Settings
            AvaloniaUiTest.Pump();
            await Task.Delay(500);
            AvaloniaUiTest.Pump();

            await Assert.That(settings.Theme).IsEqualTo(ThemePreference.Dark);
            await Assert.That(toastService.Toasts).IsEmpty();
            await Assert.That(settings.IsSaved).IsFalse();
        });
    }
}
