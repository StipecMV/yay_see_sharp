using System.ComponentModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class SettingsViewModel : LocalizedViewModelBase, IUninstallPolicy, IUpdateScheduleSettings, INotificationSettings, IBuildDirectoryPolicy
{
    private readonly ISettingsStore _settingsStore;
    private readonly IEngineDetector _engineDetector;
    private readonly INotificationService _notificationService;
    private BackendMode _backendMode = BackendMode.Demo;

    private AppSettings _lastSavedSettings;
    private IDisposable? _savePipeline;

    private string _language;
    private ThemePreference _theme;
    private CloseAction _closeAction;
    private bool _notificationsEnabled;
    private bool _removeOrphansByDefault;
    private TimeOnly _updateScheduleTime;
    private PackageManagerEngine _engine;
    private string _buildDirectory;
    private bool _autoUpdateCheckEnabled;
    private bool _isSaved;
    private IReadOnlyList<SelectableOption<string>> _languageOptions = [];

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ILocalizationService localizationService,
        AppSettings initial,
        IEngineDetector engineDetector,
        INotificationService? notificationService = null)
        : base(localizationService)
    {
        _settingsStore = settingsStore;
        _engineDetector = engineDetector;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        _language = initial.Language;
        _theme = initial.Theme;
        _closeAction = initial.CloseAction;
        _notificationsEnabled = initial.NotificationsEnabled;
        _removeOrphansByDefault = initial.RemoveOrphansByDefault;
        _updateScheduleTime = initial.UpdateScheduleTime;
        // Clamp a stale/persisted Paru preference back to Yay: only Yay is a selectable option
        // today, and leaving it unclamped would desync SelectedValue from EngineOptions.
        _engine = initial.Engine == PackageManagerEngine.Yay ? initial.Engine : PackageManagerEngine.Yay;
        _buildDirectory = initial.BuildDirectory;
        _autoUpdateCheckEnabled = initial.AutoUpdateCheckEnabled;
        _languageOptions = BuildLanguageOptions();
        ThemeOptions = BuildThemeOptions();
        CloseActionOptions = BuildCloseActionOptions();
        EngineOptions = BuildEngineOptions();

        DetectEngineCommand = ReactiveCommand.Create(DetectEngine);

        // BUGFIX-2026-08: settings auto-save runs through a short debounced pipeline instead of
        // being triggered from every setter. Load-time binding pushes (e.g. the Language ComboBox
        // briefly writing "" and then the real value while the Settings view initializes) used to
        // fire the save machinery and surface a spurious "Saved" toast even though nothing had
        // changed. The debounce collapses rapid pushes into one evaluation, and the diff against
        // the last-persisted values skips the save+toast entirely when the net result is
        // unchanged. A real user change (final values differ) still saves immediately after the
        // debounce window and shows the toast.
        _lastSavedSettings = initial;
        _savePipeline = Observable
            .FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => PropertyChanged += handler,
                handler => PropertyChanged -= handler)
            .Throttle(TimeSpan.FromMilliseconds(250), RxApp.MainThreadScheduler)
            .Subscribe(_ => RunSaveIfChangedAsync().FireAndForget());
    }

    public override void Dispose()
    {
        _savePipeline?.Dispose();
        base.Dispose();
    }

    public IReadOnlyList<SelectableOption<string>> LanguageOptions
    {
        get => _languageOptions;
        private set => this.RaiseAndSetIfChanged(ref _languageOptions, value);
    }

    public IReadOnlyList<SelectableOption<ThemePreference>> ThemeOptions { get; private set; } = [];

    public IReadOnlyList<SelectableOption<CloseAction>> CloseActionOptions { get; private set; } = [];

    public IReadOnlyList<SelectableOption<PackageManagerEngine>> EngineOptions { get; private set; } = [];

    public ReactiveCommand<Unit, Unit> DetectEngineCommand { get; }

    public string AppearanceLabel => Localization.GetString("Settings.Appearance");

    public string LanguageLabel => Localization.GetString("Settings.Language");

    public string LanguageHintLabel => Localization.GetString("Settings.Language.Hint");

    public string ThemeLabel => Localization.GetString("Settings.Theme");

    public string CloseActionLabel => Localization.GetString("Settings.CloseAction");

    public string NotificationsLabel => Localization.GetString("Settings.Notifications");

    public string NotificationsHintLabel => Localization.GetString("Settings.Notifications.Hint");

    public string RemoveOrphansLabel => Localization.GetString("Settings.RemoveOrphans");

    public string UpdateScheduleTimeLabel => Localization.GetString("Settings.UpdateScheduleTime");

    public string AutoUpdateLabel => Localization.GetString("Settings.AutoUpdate");

    /// <summary>UI-19: describes the actually-configured schedule (a daily check at UpdateScheduleTime), not a hardcoded interval that may not match reality.</summary>
    public string AutoUpdateHintLabel => string.Format(
        Localization.GetString("Settings.AutoUpdate.HintDaily"), UpdateScheduleTime.ToString("HH:mm"));

    public string MinimizeToTrayLabel => Localization.GetString("Settings.MinimizeToTray");

    public string EngineLabel => Localization.GetString("Settings.Engine");

    public string EngineHintLabel => Localization.GetString("Settings.Engine.Hint");

    public string DetectLabel => Localization.GetString("Settings.Detect");

    public string DetectResultTitleLabel => Localization.GetString("Settings.DetectResultTitle");

    public string DetectFoundYayLabel => Localization.GetString("Settings.DetectFoundYay");

    public string DetectFoundParuLabel => Localization.GetString("Settings.DetectFoundParu");

    public string DetectFoundNoneLabel => Localization.GetString("Settings.DetectFoundNone");

    public string SavedLabel => Localization.GetString("Settings.Saved");

    public string Language
    {
        get => _language;
        set
        {
            // BUGFIX-2026-08: ignore the empty-string push Avalonia's ComboBox writes during
            // view initialization (SelectedValue briefly goes null → "" before the bound value
            // lands). It's never a valid language (SetLanguage("") normalizes back to "en"), and
            // accepting it used to trigger the auto-save machinery → spurious "Saved" toast.
            if (string.IsNullOrEmpty(value) ||
                EqualityComparer<string>.Default.Equals(_language, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _language, value);
            Localization.SetLanguage(value);
        }
    }

    public ThemePreference Theme
    {
        get => _theme;
        set
        {
            if (EqualityComparer<ThemePreference>.Default.Equals(_theme, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _theme, value);
        }
    }

    public CloseAction CloseAction
    {
        get => _closeAction;
        set
        {
            if (EqualityComparer<CloseAction>.Default.Equals(_closeAction, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _closeAction, value);
            this.RaisePropertyChanged(nameof(MinimizeToTrayEnabled));
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (EqualityComparer<bool>.Default.Equals(_notificationsEnabled, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _notificationsEnabled, value);
        }
    }

    public bool RemoveOrphansByDefault
    {
        get => _removeOrphansByDefault;
        set
        {
            if (EqualityComparer<bool>.Default.Equals(_removeOrphansByDefault, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _removeOrphansByDefault, value);
        }
    }

    public TimeOnly UpdateScheduleTime
    {
        get => _updateScheduleTime;
        set
        {
            if (EqualityComparer<TimeOnly>.Default.Equals(_updateScheduleTime, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _updateScheduleTime, value);
            this.RaisePropertyChanged(nameof(UpdateScheduleTimeOfDay));
            this.RaisePropertyChanged(nameof(AutoUpdateHintLabel));
        }
    }

    public TimeSpan UpdateScheduleTimeOfDay
    {
        get => _updateScheduleTime.ToTimeSpan();
        set => UpdateScheduleTime = TimeOnly.FromTimeSpan(value);
    }

    public PackageManagerEngine Engine
    {
        get => _engine;
        set
        {
            if (EqualityComparer<PackageManagerEngine>.Default.Equals(_engine, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _engine, value);
        }
    }

    public string BuildDirectory
    {
        get => _buildDirectory;
        set
        {
            if (EqualityComparer<string>.Default.Equals(_buildDirectory, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _buildDirectory, value);
        }
    }

    public bool AutoUpdateCheckEnabled
    {
        get => _autoUpdateCheckEnabled;
        set
        {
            if (EqualityComparer<bool>.Default.Equals(_autoUpdateCheckEnabled, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _autoUpdateCheckEnabled, value);
        }
    }

    public bool MinimizeToTrayEnabled
    {
        get => CloseAction == CloseAction.HideToTray;
        set => CloseAction = value ? CloseAction.HideToTray : CloseAction.Exit;
    }

    public bool IsSaved
    {
        get => _isSaved;
        private set => this.RaiseAndSetIfChanged(ref _isSaved, value);
    }

    public AppSettings ToSettings() => new(
        Language,
        Theme,
        CloseAction,
        NotificationsEnabled,
        RemoveOrphansByDefault,
        UpdateScheduleTime,
        Engine,
        BuildDirectory,
        AutoUpdateCheckEnabled);

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(AppearanceLabel));
        this.RaisePropertyChanged(nameof(LanguageLabel));
        this.RaisePropertyChanged(nameof(LanguageHintLabel));
        this.RaisePropertyChanged(nameof(ThemeLabel));
        this.RaisePropertyChanged(nameof(CloseActionLabel));
        this.RaisePropertyChanged(nameof(NotificationsLabel));
        this.RaisePropertyChanged(nameof(NotificationsHintLabel));
        this.RaisePropertyChanged(nameof(RemoveOrphansLabel));
        this.RaisePropertyChanged(nameof(UpdateScheduleTimeLabel));
        this.RaisePropertyChanged(nameof(AutoUpdateLabel));
        this.RaisePropertyChanged(nameof(AutoUpdateHintLabel));
        this.RaisePropertyChanged(nameof(MinimizeToTrayLabel));
        this.RaisePropertyChanged(nameof(EngineLabel));
        this.RaisePropertyChanged(nameof(EngineHintLabel));
        this.RaisePropertyChanged(nameof(DetectLabel));
        this.RaisePropertyChanged(nameof(DetectResultTitleLabel));
        this.RaisePropertyChanged(nameof(DetectFoundYayLabel));
        this.RaisePropertyChanged(nameof(DetectFoundParuLabel));
        this.RaisePropertyChanged(nameof(DetectFoundNoneLabel));
        this.RaisePropertyChanged(nameof(SavedLabel));

        // Update labels in-place — do NOT replace the list instances.
        // Replacing ItemsSource causes Avalonia to reset SelectedValue before
        // re-evaluating it against the new list, which empties the ComboBox.
        foreach (var opt in LanguageOptions)
            opt.Label = Localization.GetString($"Settings.Language.{CapitalizeFirst(opt.Value)}");
        foreach (var opt in ThemeOptions)
            opt.Label = Localization.GetString($"Theme.{opt.Value}");
        foreach (var opt in CloseActionOptions)
            opt.Label = Localization.GetString($"Settings.CloseAction.{opt.Value}");
        foreach (var opt in EngineOptions)
            opt.Label = Localization.GetString($"Settings.Engine.{opt.Value}");
    }

    private IReadOnlyList<SelectableOption<string>> BuildLanguageOptions() => Localization.AvailableLanguages
        .Select(code => new SelectableOption<string>(code, Localization.GetString($"Settings.Language.{CapitalizeFirst(code)}")))
        .ToArray();

    private IReadOnlyList<SelectableOption<ThemePreference>> BuildThemeOptions() => Enum.GetValues<ThemePreference>()
        .Select(value => new SelectableOption<ThemePreference>(value, Localization.GetString($"Theme.{value}")))
        .ToArray();

    private IReadOnlyList<SelectableOption<CloseAction>> BuildCloseActionOptions() => Enum.GetValues<CloseAction>()
        .Select(value => new SelectableOption<CloseAction>(value, Localization.GetString($"Settings.CloseAction.{value}")))
        .ToArray();

    // Only yay is implemented (see PackageBackendFactory.Create). PackageManagerEngine.Paru
    // still exists on the enum for when a ParuPackageBackend lands, but it's deliberately left
    // out of the selectable options so the UI can't offer an engine the app can't actually run.
    private IReadOnlyList<SelectableOption<PackageManagerEngine>> BuildEngineOptions() =>
    [
        new(PackageManagerEngine.Yay, Localization.GetString($"Settings.Engine.{PackageManagerEngine.Yay}")),
    ];

    /// <summary>
    /// BUGFIX-2026-08: Detect always reports what it actually found (a "Detection result" toast),
    /// instead of silently applying the engine and letting the auto-save machinery surface a
    /// confusing "Saved" toast — or, in Real mode with yay already configured, nothing at all.
    /// </summary>
    private void DetectEngine()
    {
        // UI-17: in Demo/Simulated mode the running backend is fixed for this session regardless
        // of what's on PATH — telling the user a real detection ran (and found nothing, or found
        // something irrelevant) would be misleading, so it's short-circuited to an explanatory
        // toast instead of actually invoking IEngineDetector.
        if (_backendMode != BackendMode.Real)
        {
            _notificationService.SendAsync(
                Localization.GetString("Settings.SimulatedModeTitle"),
                Localization.GetString("Settings.SimulatedModeDetect"),
                NotificationLevel.Info).FireAndForget();
            return;
        }

        // Detection can still report Paru if that's what's on PATH, but there's nothing to
        // switch to yet, so only a Yay result is applied (and even then Engine is already Yay in
        // practice — the assignment is defensive, not a real change).
        var detected = _engineDetector.Detect();
        if (detected is PackageManagerEngine.Yay)
        {
            Engine = PackageManagerEngine.Yay;
            _notificationService.SendAsync(
                DetectResultTitleLabel, DetectFoundYayLabel, NotificationLevel.Success).FireAndForget();
        }
        else if (detected is PackageManagerEngine.Paru)
        {
            _notificationService.SendAsync(
                DetectResultTitleLabel, DetectFoundParuLabel, NotificationLevel.Info).FireAndForget();
        }
        else
        {
            _notificationService.SendAsync(
                DetectResultTitleLabel, DetectFoundNoneLabel, NotificationLevel.Warning).FireAndForget();
        }
    }

    /// <summary>UI-18: called from the Engine picker's Yay option when clicked in Demo/Simulated mode (there's nothing to actually switch to — the running backend is fixed for this session).</summary>
    public void NotifyIfSimulated()
    {
        if (_backendMode != BackendMode.Real)
        {
            _notificationService.SendAsync(
                Localization.GetString("Settings.SimulatedModeTitle"),
                Localization.GetString("Settings.SimulatedModeYay"),
                NotificationLevel.Info).FireAndForget();
        }
    }

    /// <summary>Set once by AppBootstrapper after the real backend is resolved — SettingsViewModel itself is constructed earlier, before the backend exists, so this can't be a constructor parameter.</summary>
    public void SetBackendMode(BackendMode mode) => _backendMode = mode;

    private static string CapitalizeFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>
    /// BUGFIX-2026-08: the debounced auto-save evaluation. Compares the current values against
    /// the last-persisted snapshot and skips the write (and the "Saved" toast) when nothing
    /// actually changed — load-time binding pushes like the Language ComboBox briefly writing ""
    /// then the real value collapse inside the 250ms debounce and net out to "no change".
    /// When something did change, saves once and notifies, exactly like the old immediate path.
    /// </summary>
    private async Task RunSaveIfChangedAsync()
    {
        var current = ToSettings();
        if (current.Equals(_lastSavedSettings))
        {
            return;
        }

        await _settingsStore.SaveAsync(current);
        _lastSavedSettings = current;
        IsSaved = true;

        // UI-15: "Saved" is a transient toast, not a permanently-visible label — SettingsView no
        // longer renders SavedLabel itself; IsSaved/SavedLabel stay for anything else that wants
        // to know a save just completed (e.g. tests).
        await _notificationService.SendAsync(SavedLabel, string.Empty, NotificationLevel.Success);
    }
}
