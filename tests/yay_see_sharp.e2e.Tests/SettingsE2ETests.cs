using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ReactiveUI;
using yay_see_sharp.domain.Models;

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
}
