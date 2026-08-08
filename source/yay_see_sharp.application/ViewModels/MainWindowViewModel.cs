using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using yay_see_sharp.application.Platform;
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
    private BackendInstallPromptViewModel? _backendInstallPrompt;
    private readonly IBackendInstaller? _backendInstaller;

    public MainWindowViewModel(
        IPackageBackend backend,
        ILocalizationService localizationService,
        SettingsViewModel settings,
        DashboardViewModel dashboard,
        SearchViewModel search,
        InstalledPackagesViewModel installed,
        IBackendInstaller? backendInstaller = null,
        ToastService? toastService = null)
        : base(localizationService)
    {
        Backend = backend;
        _backendInstaller = backendInstaller;
        Loading = new LoadingViewModel(localizationService);
        Settings = settings;
        Dashboard = dashboard;
        Search = search;
        Installed = installed;
        Toasts = (toastService ?? new ToastService()).Toasts;

        NavigationItems =
        [
            new NavigationItemViewModel(NavigationSection.Dashboard, "Navigation.Dashboard", localizationService),
            new NavigationItemViewModel(NavigationSection.Search, "Navigation.Search", localizationService),
            new NavigationItemViewModel(NavigationSection.Installed, "Navigation.Installed", localizationService),
            new NavigationItemViewModel(NavigationSection.Settings, "Navigation.Settings", localizationService),
        ];
        // Computed once and cached (not a `=>` getter) so a ListBox's ItemsSource binding and a
        // later `viewModel.MainNavigationItems` read (e.g. in a test locating that same ListBox)
        // are guaranteed to be the exact same collection instance — a fresh array on every access
        // would never ReferenceEquals what's actually bound.
        MainNavigationItems = NavigationItems.Where(item => item.Section != NavigationSection.Settings).ToArray();
        SettingsNavigationItem = NavigationItems.Single(item => item.Section == NavigationSection.Settings);
        SelectSettingsCommand = ReactiveCommand.Create(() => { SelectedNavigationItem = SettingsNavigationItem; });

        // UI-05: clicking a dashboard update row navigates to that package's detail on Installed.
        Dashboard.PackageActivated += (_, name) =>
        {
            SelectedNavigationItem = NavigationItems.Single(item => item.Section == NavigationSection.Installed);
            Installed.SelectByName(name);
        };

        _selectedNavigationItem = NavigationItems[0];
        _currentPage = Loading;

        // UI-14: the bottom Settings row is a plain button (not a second ListBox two-way-bound to
        // the same SelectedNavigationItem) — two selection-bound controls sharing one property
        // with disjoint ItemsSources fight each other: whichever control's binding reacts to the
        // other's "item not in my list" case last wins, so clicking Settings could get silently
        // reverted by the main list's own binding. IsSettingsSelected drives the row's selected
        // visual state directly instead.
        //
        // BUGFIX-2026-08: navigating to Installed or Dashboard re-fetches their data. Installing
        // a package from Search (or updating from Dashboard) previously left Installed showing a
        // stale snapshot — "installed htop, it never appeared in Installed". A refresh on entry
        // is the single hook that covers every mutation path, including ones made outside the
        // app (terminal, other package tools).
        this.WhenAnyValue(x => x.SelectedNavigationItem)
            .Subscribe(item =>
            {
                CurrentPage = ResolvePage(item);
                if (item.Section == NavigationSection.Installed)
                {
                    Installed.RefreshAsync().FireAndForget();
                }
                else if (item.Section == NavigationSection.Dashboard)
                {
                    Dashboard.RefreshAsync().FireAndForget();
                }

                this.RaisePropertyChanged(nameof(IsSettingsSelected));
            });

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

        if (_backendInstaller is not null && Mode == BackendMode.Unavailable)
        {
            await RequestBackendInstallAsync();
        }
    }

    /// <summary>Shows the "yay is missing, install it?" overlay. Resolves once the user confirms/dismisses/closes it — a successful install still requires a restart to actually switch backends (see AppBootstrapper), which the prompt's own text explains.</summary>
    private async Task RequestBackendInstallAsync()
    {
        var prompt = new BackendInstallPromptViewModel(_backendInstaller!, Localization);
        BackendInstallPrompt = prompt;
        await prompt.WaitForResultAsync();
        BackendInstallPrompt = null;
    }

    public LoadingViewModel Loading { get; }

    public DashboardViewModel Dashboard { get; }

    public SearchViewModel Search { get; }

    public InstalledPackagesViewModel Installed { get; }

    public SettingsViewModel Settings { get; }

    /// <summary>UI-22: the live in-app toast stack, bound to an overlay in MainWindow. Backed by the same ToastService instance AppBootstrapper fans real notifications into, so every INotificationService.SendAsync call across the app shows up here too.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; }

    public IPackageBackend Backend { get; }

    public BackendInfo BackendInfo => Backend.Info;

    public string DistributionName => BackendInfo.DistributionName;

    public string PackageManager => BackendInfo.PackageManager;

    public bool ShowPackageManager => Mode == BackendMode.Real;

    public BackendMode Mode => BackendInfo.Mode;

    public string ModeLabel => Localization.GetString(Mode switch
    {
        BackendMode.Real => "Backend.ModeReal",
        BackendMode.Unavailable => "Backend.ModeUnavailable",
        _ => "Backend.ModeDemo",
    });

    public bool IsSupported => BackendInfo.IsSupported;

    public bool HasWarning => !IsSupported;

    public string? WarningMessage => Mode switch
    {
        BackendMode.Unavailable => Localization.GetString("Dashboard.YayMissingWarning"),
        _ when HasWarning => string.Format(Localization.GetString("Dashboard.UnsupportedWarning"), DistributionName),
        _ => null,
    };

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    /// <summary>UI-14: Settings lives in its own sidebar group, pinned to the bottom — everything else in <see cref="NavigationItems"/> except it.</summary>
    public IReadOnlyList<NavigationItemViewModel> MainNavigationItems { get; }

    /// <summary>UI-14: the Settings nav entry, shown as its own row pinned to the sidebar's bottom.</summary>
    public NavigationItemViewModel SettingsNavigationItem { get; }

    public bool IsSettingsSelected => SelectedNavigationItem.Section == NavigationSection.Settings;

    public ReactiveCommand<Unit, Unit> SelectSettingsCommand { get; }

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

    /// <summary>Non-null while the "yay is missing, install it?" overlay is shown — see <see cref="FinishLoadingAsync"/>.</summary>
    public BackendInstallPromptViewModel? BackendInstallPrompt
    {
        get => _backendInstallPrompt;
        private set => this.RaiseAndSetIfChanged(ref _backendInstallPrompt, value);
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
