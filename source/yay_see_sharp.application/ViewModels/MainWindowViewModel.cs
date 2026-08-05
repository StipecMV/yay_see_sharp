using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// The app shell. Deliberately takes every child ViewModel pre-built via the constructor rather
/// than constructing them (or the services behind them, e.g. notifications/command running)
/// itself — that composition-root wiring lives in AppBootstrapper, so this class stays a plain
/// assembler/navigator that's trivial to unit test with hand-built child ViewModels.
/// </summary>
public class MainWindowViewModel : LocalizedViewModelBase
{
    private NavigationItemViewModel _selectedNavigationItem;
    private ViewModelBase _currentPage;
    private AuthPromptViewModel? _authPrompt;

    public MainWindowViewModel(
        IPackageBackend backend,
        ILocalizationService localizationService,
        SettingsViewModel settings,
        DashboardViewModel dashboard,
        SearchViewModel search,
        InstalledPackagesViewModel installed)
        : base(localizationService)
    {
        Backend = backend;
        Loading = new LoadingViewModel(localizationService);
        Settings = settings;
        Dashboard = dashboard;
        Search = search;
        Installed = installed;

        NavigationItems =
        [
            new NavigationItemViewModel(NavigationSection.Dashboard, "Navigation.Dashboard", localizationService),
            new NavigationItemViewModel(NavigationSection.Search, "Navigation.Search", localizationService),
            new NavigationItemViewModel(NavigationSection.Installed, "Navigation.Installed", localizationService),
            new NavigationItemViewModel(NavigationSection.Settings, "Navigation.Settings", localizationService),
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentPage = Loading;

        this.WhenAnyValue(x => x.SelectedNavigationItem)
            .Subscribe(item => CurrentPage = ResolvePage(item));

        Settings.WhenAnyValue(x => x.Theme).Subscribe(_ =>
        {
            this.RaisePropertyChanged(nameof(Theme));
            this.RaisePropertyChanged(nameof(ThemeLabel));
        });

        FinishLoadingAsync().FireAndForget();
    }

    private async Task FinishLoadingAsync()
    {
        await Dashboard.InitialLoadTask;
        CurrentPage = ResolvePage(SelectedNavigationItem);
    }

    public LoadingViewModel Loading { get; }

    public DashboardViewModel Dashboard { get; }

    public SearchViewModel Search { get; }

    public InstalledPackagesViewModel Installed { get; }

    public SettingsViewModel Settings { get; }

    public IPackageBackend Backend { get; }

    public BackendInfo BackendInfo => Backend.Info;

    public string DistributionName => BackendInfo.DistributionName;

    public string PackageManager => BackendInfo.PackageManager;

    public bool ShowPackageManager => Mode == BackendMode.Real;

    public BackendMode Mode => BackendInfo.Mode;

    public string ModeLabel => Localization.GetString(
        Mode == BackendMode.Real ? "Backend.ModeReal" : "Backend.ModeDemo");

    public bool IsSupported => BackendInfo.IsSupported;

    public bool HasWarning => !IsSupported;

    public string? WarningMessage => HasWarning
        ? string.Format(Localization.GetString("Dashboard.UnsupportedWarning"), DistributionName)
        : null;

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    public ThemePreference Theme => Settings.Theme;

    public string ThemeLabel => Localization.GetString($"Theme.{Theme}");

    public NavigationItemViewModel SelectedNavigationItem
    {
        get => _selectedNavigationItem;
        set => this.RaiseAndSetIfChanged(ref _selectedNavigationItem, value);
    }

    public ViewModelBase CurrentPage
    {
        get => _currentPage;
        set => this.RaiseAndSetIfChanged(ref _currentPage, value);
    }

    /// <summary>
    /// Non-null while a privileged operation is waiting on the password prompt modal.
    /// Backends that need elevation call <see cref="RequestAuthenticationAsync"/> to surface it.
    /// </summary>
    public AuthPromptViewModel? AuthPrompt
    {
        get => _authPrompt;
        private set => this.RaiseAndSetIfChanged(ref _authPrompt, value);
    }

    public async Task<string?> RequestAuthenticationAsync()
    {
        var prompt = new AuthPromptViewModel(Localization);
        AuthPrompt = prompt;
        var result = await prompt.WaitForResultAsync();
        AuthPrompt = null;
        return result;
    }

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(ModeLabel));
        this.RaisePropertyChanged(nameof(ThemeLabel));
        this.RaisePropertyChanged(nameof(WarningMessage));
    }

    private ViewModelBase ResolvePage(NavigationItemViewModel item) => item.Section switch
    {
        NavigationSection.Dashboard => Dashboard,
        NavigationSection.Search => Search,
        NavigationSection.Installed => Installed,
        NavigationSection.Settings => Settings,
        _ => item,
    };
}
