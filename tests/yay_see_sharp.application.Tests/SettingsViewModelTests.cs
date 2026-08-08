using Moq;
using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
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
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>());

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
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>());

        try
        {
            await Assert.That(viewModel.IsSaved).IsFalse();

            // BUGFIX-2026-08: AppSettings.Default.NotificationsEnabled is now false (desktop
            // notifications off by default), so `NotificationsEnabled = false` is a no-op that
            // would never trigger a save — flip it to true to represent a real change.
            viewModel.NotificationsEnabled = true;
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
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>());

        viewModel.UpdateScheduleTimeOfDay = new TimeSpan(9, 15, 0);

        await Assert.That(viewModel.UpdateScheduleTime).IsEqualTo(new TimeOnly(9, 15));
    }

    [Test]
    public async Task Changing_language_property_switches_immediately_and_auto_saves()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var localization = new LocalizationService("en");
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>());

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
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>());

        var systemTheme = viewModel.ThemeOptions.Single(option => option.Value == ThemePreference.System);
        await Assert.That(systemTheme.Label).IsEqualTo("System");
        var hideToTray = viewModel.CloseActionOptions.Single(option => option.Value == CloseAction.HideToTray);
        await Assert.That(hideToTray.Label).IsEqualTo("Hide to tray");
        var englishOption = viewModel.LanguageOptions.Single(option => option.Value == "en");
        await Assert.That(englishOption.Label).IsEqualTo("English");
        // BUGFIX/feature 2026-08: German and Polish are selectable languages too.
        var germanOption = viewModel.LanguageOptions.Single(option => option.Value == "de");
        await Assert.That(germanOption.Label).IsEqualTo("German");
        var polishOption = viewModel.LanguageOptions.Single(option => option.Value == "pl");
        await Assert.That(polishOption.Label).IsEqualTo("Polish");

        localization.SetLanguage("sk");

        var systemThemeSk = viewModel.ThemeOptions.Single(option => option.Value == ThemePreference.System);
        await Assert.That(systemThemeSk.Label).IsEqualTo("Systémová");
        var hideToTraySk = viewModel.CloseActionOptions.Single(option => option.Value == CloseAction.HideToTray);
        await Assert.That(hideToTraySk.Label).IsEqualTo("Skryť do systémovej lišty");
        await Assert.That(germanOption.Label).IsEqualTo("Nemčina");
        await Assert.That(polishOption.Label).IsEqualTo("Poľština");

        localization.SetLanguage("de");

        await Assert.That(englishOption.Label).IsEqualTo("Englisch");
        await Assert.That(germanOption.Label).IsEqualTo("Deutsch");
        await Assert.That(polishOption.Label).IsEqualTo("Polnisch");

        localization.SetLanguage("pl");

        await Assert.That(englishOption.Label).IsEqualTo("Angielski");
        await Assert.That(germanOption.Label).IsEqualTo("Niemiecki");
        await Assert.That(polishOption.Label).IsEqualTo("Polski");
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
        var viewModel = new SettingsViewModel(store, localization, AppSettings.Default, Mock.Of<IEngineDetector>());

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
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>());

        await Assert.That(viewModel.EngineOptions.Count).IsEqualTo(1);
        await Assert.That(viewModel.EngineOptions[0].Value).IsEqualTo(PackageManagerEngine.Yay);
    }

    [Test]
    public async Task A_persisted_paru_preference_is_clamped_back_to_yay_on_load()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var persisted = AppSettings.Default with { Engine = PackageManagerEngine.Paru };

        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), persisted, Mock.Of<IEngineDetector>());

        await Assert.That(viewModel.Engine).IsEqualTo(PackageManagerEngine.Yay);
    }

    // --- BUGFIX-2026-08: Detect reports what it found; "Saved" only after a real change ---

    [Test]
    public async Task Detect_in_real_mode_reports_the_found_engine_instead_of_a_save_toast()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var notifications = new RecordingNotificationService();
        var detector = new Mock<IEngineDetector>();
        detector.Setup(d => d.Detect()).Returns(PackageManagerEngine.Yay);
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, detector.Object, notifications);
        viewModel.SetBackendMode(BackendMode.Real);

        viewModel.DetectEngineCommand.Execute().Subscribe();

        await Assert.That(notifications.Sent.Count).IsEqualTo(1);
        await Assert.That(notifications.Sent[0].Title).IsEqualTo("Detection result");
        await Assert.That(notifications.Sent[0].Body).Contains("yay");
        await Assert.That(notifications.Sent[0].Level).IsEqualTo(NotificationLevel.Success);
        // Detect must not trigger an auto-save ("Saved" toast) when nothing changed.
        await Assert.That(viewModel.IsSaved).IsFalse();
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task Detect_reports_paru_and_nothing_found_without_saving()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var notifications = new RecordingNotificationService();
        var detector = new Mock<IEngineDetector>();
        detector.Setup(d => d.Detect()).Returns(PackageManagerEngine.Paru);
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, detector.Object, notifications);
        viewModel.SetBackendMode(BackendMode.Real);

        viewModel.DetectEngineCommand.Execute().Subscribe();

        await Assert.That(notifications.Sent.Count).IsEqualTo(1);
        await Assert.That(notifications.Sent[0].Title).IsEqualTo("Detection result");
        await Assert.That(notifications.Sent[0].Body).Contains("paru");
        await Assert.That(notifications.Sent[0].Level).IsEqualTo(NotificationLevel.Info);
        await Assert.That(File.Exists(path)).IsFalse();

        notifications.Sent.Clear();
        detector.Setup(d => d.Detect()).Returns((PackageManagerEngine?)null);

        viewModel.DetectEngineCommand.Execute().Subscribe();

        await Assert.That(notifications.Sent.Count).IsEqualTo(1);
        await Assert.That(notifications.Sent[0].Body).Contains("No supported package manager engine");
        await Assert.That(notifications.Sent[0].Level).IsEqualTo(NotificationLevel.Warning);
        await Assert.That(File.Exists(path)).IsFalse();
    }

    [Test]
    public async Task Detect_in_simulated_mode_explains_detection_is_unavailable()
    {
        var store = new FileSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
        var notifications = new RecordingNotificationService();
        var detector = new Mock<IEngineDetector>();
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, detector.Object, notifications);
        // Mode stays Demo (SetBackendMode never called / non-Real).

        viewModel.DetectEngineCommand.Execute().Subscribe();

        await Assert.That(notifications.Sent.Count).IsEqualTo(1);
        await Assert.That(notifications.Sent[0].Title).IsEqualTo("Simulated mode");
        detector.Verify(d => d.Detect(), Times.Never);
    }

    [Test]
    public async Task Saved_toast_only_fires_after_an_actual_change()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var notifications = new RecordingNotificationService();
        var viewModel = new SettingsViewModel(store, new LocalizationService("en"), AppSettings.Default, Mock.Of<IEngineDetector>(), notifications);

        // Constructed with no changes → nothing saved, no toast (regression: spurious
        // load-time pushes must not surface a "Saved" toast).
        await Assert.That(notifications.Sent).IsEmpty();

        viewModel.Theme = ThemePreference.Dark;
        await viewModel.WhenAnyValue(x => x.IsSaved).FirstAsync(saved => saved);

        // The toast is sent right after IsSaved flips (the save loop's last step), so poll
        // briefly instead of asserting immediately — the two are deliberately not atomic.
        await WaitUntilAsync(() => notifications.Sent.Count == 1, TimeSpan.FromSeconds(2));

        await Assert.That(notifications.Sent.Count).IsEqualTo(1);
        await Assert.That(notifications.Sent[0].Title).IsEqualTo("Saved");
        await Assert.That(notifications.Sent[0].Level).IsEqualTo(NotificationLevel.Success);
    }

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

    /// <summary>Records every notification a ViewModel sent — no OS/UI side effects.</summary>
    private sealed class RecordingNotificationService : INotificationService
    {
        public List<(string Title, string Body, NotificationLevel Level)> Sent { get; } = [];

        public Task SendAsync(
            string title,
            string body,
            NotificationLevel level = NotificationLevel.Info,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((title, body, level));
            return Task.CompletedTask;
        }
    }

}
