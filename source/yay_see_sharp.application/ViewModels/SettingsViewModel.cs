using System.Linq;
using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.application.ViewModels;

public class SettingsViewModel : LocalizedViewModelBase, IUninstallPolicy, IUpdateScheduleSettings, INotificationSettings, IBuildDirectoryPolicy
{
    private readonly ISettingsStore _settingsStore;
    private readonly IEngineDetector _engineDetector;
    private readonly IFolderBrowserService _folderBrowserService;

    private Task? _pendingSave;
    private bool _dirtyWhileSaving;

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
    private FolderBrowserViewModel? _folderBrowser;

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ILocalizationService localizationService,
        AppSettings initial,
        IEngineDetector engineDetector,
        IFolderBrowserService folderBrowserService)
        : base(localizationService)
    {
        _settingsStore = settingsStore;
        _engineDetector = engineDetector;
        _folderBrowserService = folderBrowserService;
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
        BrowseCommand = ReactiveCommand.CreateFromTask(BrowseForBuildDirectoryAsync);
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

    public ReactiveCommand<Unit, Unit> BrowseCommand { get; }

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

    public string AutoUpdateHintLabel => Localization.GetString("Settings.AutoUpdate.Hint");

    public string MinimizeToTrayLabel => Localization.GetString("Settings.MinimizeToTray");

    public string EngineLabel => Localization.GetString("Settings.Engine");

    public string EngineHintLabel => Localization.GetString("Settings.Engine.Hint");

    public string DetectLabel => Localization.GetString("Settings.Detect");

    public string AurHelperLabel => Localization.GetString("Settings.AurHelper");

    public string BuildDirectoryLabel => Localization.GetString("Settings.BuildDirectory");

    public string BrowseLabel => Localization.GetString("Settings.Browse");

    public string SavedLabel => Localization.GetString("Settings.Saved");

    public string Language
    {
        get => _language;
        set
        {
            this.RaiseAndSetIfChanged(ref _language, value);
            Localization.SetLanguage(value);
            TriggerAutoSave();
        }
    }

    public ThemePreference Theme
    {
        get => _theme;
        set
        {
            this.RaiseAndSetIfChanged(ref _theme, value);
            TriggerAutoSave();
        }
    }

    public CloseAction CloseAction
    {
        get => _closeAction;
        set
        {
            this.RaiseAndSetIfChanged(ref _closeAction, value);
            this.RaisePropertyChanged(nameof(MinimizeToTrayEnabled));
            TriggerAutoSave();
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _notificationsEnabled, value);
            TriggerAutoSave();
        }
    }

    public bool RemoveOrphansByDefault
    {
        get => _removeOrphansByDefault;
        set
        {
            this.RaiseAndSetIfChanged(ref _removeOrphansByDefault, value);
            TriggerAutoSave();
        }
    }

    public TimeOnly UpdateScheduleTime
    {
        get => _updateScheduleTime;
        set
        {
            this.RaiseAndSetIfChanged(ref _updateScheduleTime, value);
            this.RaisePropertyChanged(nameof(UpdateScheduleTimeOfDay));
            TriggerAutoSave();
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
            this.RaiseAndSetIfChanged(ref _engine, value);
            TriggerAutoSave();
        }
    }

    public string BuildDirectory
    {
        get => _buildDirectory;
        set
        {
            this.RaiseAndSetIfChanged(ref _buildDirectory, value);
            TriggerAutoSave();
        }
    }

    public bool AutoUpdateCheckEnabled
    {
        get => _autoUpdateCheckEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _autoUpdateCheckEnabled, value);
            TriggerAutoSave();
        }
    }

    public bool MinimizeToTrayEnabled
    {
        get => CloseAction == CloseAction.HideToTray;
        set => CloseAction = value ? CloseAction.HideToTray : CloseAction.Exit;
    }

    public FolderBrowserViewModel? FolderBrowser
    {
        get => _folderBrowser;
        private set => this.RaiseAndSetIfChanged(ref _folderBrowser, value);
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
        this.RaisePropertyChanged(nameof(AurHelperLabel));
        this.RaisePropertyChanged(nameof(BuildDirectoryLabel));
        this.RaisePropertyChanged(nameof(BrowseLabel));
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

    private void DetectEngine()
    {
        // Detection can still report Paru if that's what's on PATH, but there's nothing to
        // switch to yet, so only a Yay result is applied.
        if (_engineDetector.Detect() is PackageManagerEngine.Yay)
        {
            Engine = PackageManagerEngine.Yay;
        }
    }

    private async Task BrowseForBuildDirectoryAsync()
    {
        var browser = new FolderBrowserViewModel(BuildDirectory, Localization, _folderBrowserService);
        FolderBrowser = browser;
        var result = await browser.WaitForResultAsync();
        FolderBrowser = null;

        if (result is not null)
        {
            BuildDirectory = result;
        }
    }

    private static string CapitalizeFirst(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>
    /// Coalesces rapid successive changes into a single save loop: if a change happens
    /// while a save is already in flight, it is marked dirty and re-saved (reading the
    /// then-current values) once the in-flight save finishes, instead of starting a second
    /// concurrent write to the same file (which both corrupts write ordering and throws
    /// when two writers race for the same file handle).
    /// </summary>
    private void TriggerAutoSave()
    {
        IsSaved = false;

        if (_pendingSave is { IsCompleted: false })
        {
            _dirtyWhileSaving = true;
            return;
        }

        _pendingSave = RunSaveLoopAsync();
    }

    private async Task RunSaveLoopAsync()
    {
        do
        {
            _dirtyWhileSaving = false;
            await _settingsStore.SaveAsync(ToSettings());
        }
        while (_dirtyWhileSaving);

        IsSaved = true;
    }
}
