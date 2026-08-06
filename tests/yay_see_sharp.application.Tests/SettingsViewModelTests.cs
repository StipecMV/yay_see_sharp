using Moq;
using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Platform;
using yay_see_sharp.infrastructure.Settings;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class SettingsViewModelTests
{
    [Test]
    public async Task Changing_a_value_auto_saves_without_a_save_button()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var localization = new LocalizationService("en");
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        try
        {
            viewModel.Theme = ThemePreference.Dark;
            viewModel.CloseAction = CloseAction.Exit;
            viewModel.NotificationsEnabled = false;
            viewModel.RemoveOrphansByDefault = false;
            viewModel.UpdateScheduleTime = new TimeOnly(14, 30);

            await viewModel.WhenAnyValue(x => x.IsSaved).FirstAsync(saved => saved);

            var persisted = await store.LoadAsync();
            await Assert.That(persisted.Theme).IsEqualTo(ThemePreference.Dark);
            await Assert.That(persisted.CloseAction).IsEqualTo(CloseAction.Exit);
            await Assert.That(persisted.NotificationsEnabled).IsFalse();
            await Assert.That(persisted.RemoveOrphansByDefault).IsFalse();
            await Assert.That(persisted.UpdateScheduleTime).IsEqualTo(new TimeOnly(14, 30));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task IsSaved_becomes_true_after_a_change_as_a_save_notification()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        try
        {
            await Assert.That(viewModel.IsSaved).IsFalse();

            viewModel.NotificationsEnabled = false;
            await viewModel.WhenAnyValue(x => x.IsSaved).FirstAsync(saved => saved);

            await Assert.That(viewModel.IsSaved).IsTrue();
            await Assert.That(viewModel.SavedLabel).IsEqualTo("Saved");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Update_schedule_time_of_day_proxy_stays_in_sync_with_time_only()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        viewModel.UpdateScheduleTimeOfDay = new TimeSpan(9, 15, 0);

        await Assert.That(viewModel.UpdateScheduleTime).IsEqualTo(new TimeOnly(9, 15));
    }

    [Test]
    public async Task Changing_language_property_switches_immediately_and_auto_saves()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var localization = new LocalizationService("en");
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        try
        {
            viewModel.Language = "sk";

            await Assert.That(localization.Language).IsEqualTo("sk");

            await viewModel.WhenAnyValue(x => x.IsSaved).FirstAsync(saved => saved);
            var persisted = await store.LoadAsync();
            await Assert.That(persisted.Language).IsEqualTo("sk");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Theme_and_close_action_options_are_localized_and_switch_live()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var localization = new LocalizationService("en");
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        var systemTheme = viewModel.ThemeOptions.Single(option => option.Value == ThemePreference.System);
        await Assert.That(systemTheme.Label).IsEqualTo("System");
        var hideToTray = viewModel.CloseActionOptions.Single(option => option.Value == CloseAction.HideToTray);
        await Assert.That(hideToTray.Label).IsEqualTo("Hide to tray");
        var englishOption = viewModel.LanguageOptions.Single(option => option.Value == "en");
        await Assert.That(englishOption.Label).IsEqualTo("English");

        localization.SetLanguage("sk");

        var systemThemeSk = viewModel.ThemeOptions.Single(option => option.Value == ThemePreference.System);
        await Assert.That(systemThemeSk.Label).IsEqualTo("Systémová");
        var hideToTraySk = viewModel.CloseActionOptions.Single(option => option.Value == CloseAction.HideToTray);
        await Assert.That(hideToTraySk.Label).IsEqualTo("Skryť do systémovej lišty");
    }
    [Test]
    public async Task Language_switch_updates_labels_in_place_without_replacing_option_instances()
    {
        // Regression test: replacing ItemsSource instances caused Avalonia ComboBox
        // to reset SelectedValue before re-evaluating it — visually emptying the combo.
        // Fix: labels are mutated in-place on existing SelectableOption instances,
        // the list references themselves are never replaced.
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var localization = new LocalizationService("en");
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        // Capture the LIST references (not .ToArray() copies) and individual items BEFORE switch
        var themeListBefore = viewModel.ThemeOptions;
        var closeListBefore = viewModel.CloseActionOptions;
        var langListBefore = viewModel.LanguageOptions;
        var systemItemBefore = viewModel.ThemeOptions.Single(o => o.Value == ThemePreference.System);
        var selectedThemeBefore = viewModel.Theme;
        var selectedCloseBefore = viewModel.CloseAction;
        var selectedLangBefore = viewModel.Language;

        localization.SetLanguage("sk");

        // List references must be the same object (not replaced)
        await Assert.That(ReferenceEquals(viewModel.ThemeOptions, themeListBefore)).IsTrue();
        await Assert.That(ReferenceEquals(viewModel.CloseActionOptions, closeListBefore)).IsTrue();
        await Assert.That(ReferenceEquals(viewModel.LanguageOptions, langListBefore)).IsTrue();

        // Individual item instances must also be the same objects
        var systemItemAfter = viewModel.ThemeOptions.Single(o => o.Value == ThemePreference.System);
        await Assert.That(ReferenceEquals(systemItemAfter, systemItemBefore)).IsTrue();

        // Underlying scalar values unchanged (ComboBox selection must survive)
        await Assert.That(viewModel.Theme).IsEqualTo(selectedThemeBefore);
        await Assert.That(viewModel.CloseAction).IsEqualTo(selectedCloseBefore);
        await Assert.That(viewModel.Language).IsEqualTo(selectedLangBefore);

        // Labels did change on the same instances
        await Assert.That(systemItemAfter.Label).IsNotEqualTo("System");
    }

    [Test]
    public async Task Engine_options_only_offer_yay_since_paru_has_no_backend_implementation_yet()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        await Assert.That(viewModel.EngineOptions.Count).IsEqualTo(1);
        await Assert.That(viewModel.EngineOptions[0].Value).IsEqualTo(PackageManagerEngine.Yay);
    }

    [Test]
    public async Task A_persisted_paru_preference_is_clamped_back_to_yay_on_load()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var persisted = AppSettings.Default with { Engine = PackageManagerEngine.Paru };

        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), persisted, Mock.Of<IEngineDetector>(), Mock.Of<IFolderBrowserService>());

        await Assert.That(viewModel.Engine).IsEqualTo(PackageManagerEngine.Yay);
    }

}
