using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class SearchViewModel : LocalizedViewModelBase
{
    /// <summary>UI-08: shown as search results whenever the query is empty, so the screen is never just a blank box — Demo mode only shows the ones present in its own catalog; Real mode searches for all of them via the real backend.</summary>
    private static readonly string[] RecommendedPackageNames =
    [
        "firefox", "vlc", "git", "neovim", "code", "gimp", "inkscape", "libreoffice-fresh",
        "steam", "discord", "obs-studio", "htop", "btop", "fzf", "ripgrep", "bat",
    ];

    private readonly IPackageBackend _backend;
    private readonly IPkgbuildService _pkgbuildService;
    private readonly IUninstallPolicy? _uninstallPolicy;
    private readonly INotificationService? _notificationService;
    private string _query = string.Empty;
    private SelectableOption<PackageSource?> _selectedSourceOption;
    private PackageSummary? _selectedPackage;
    private PackageDetailsViewModel? _selectedDetails;
    private bool _isBusy;
    private string? _errorMessage;
    private IReadOnlyList<SelectableOption<PackageSource?>> _sourceOptions;

    public SearchViewModel(
        IPackageBackend backend,
        ILocalizationService localization,
        IPkgbuildService pkgbuildService,
        IUninstallPolicy? uninstallPolicy = null,
        INotificationService? notificationService = null)
        : base(localization)
    {
        _backend = backend;
        _pkgbuildService = pkgbuildService;
        _uninstallPolicy = uninstallPolicy;
        _notificationService = notificationService;
        _sourceOptions = BuildSourceOptions();
        _selectedSourceOption = _sourceOptions[0];
        SearchCommand = ReactiveCommand.CreateFromTask(SearchAsync);
        InstallCommand = ReactiveCommand.CreateFromTask<PackageSummary>(InstallFromRowAsync);
        SelectPackageCommand = ReactiveCommand.Create<PackageSummary>(package => SelectedPackage = package);
        this.WhenAnyValue(x => x.SelectedPackage).Subscribe(OnSelectedPackageChanged);

        // UI-07/ BUGFIX-2026-08: live search — the query (and filter, so changing either re-runs
        // the same search) drives the results automatically, debounced so fast typing doesn't
        // fire a search per keystroke. The filter is observed via SelectedSourceOption (which
        // raises PropertyChanged) rather than the computed SourceFilter (which never notifies —
        // that made filter clicks appear to do nothing until the next keystroke). Both selectors
        // are projected to their *values* before DistinctUntilChanged: RaiseLocalizedPropertiesChanged
        // reassigns SelectedSourceOption (a new SelectableOption instance, same Value) on language
        // switches, and that must not look like a filter change. Skip(1) deliberately excludes the
        // constructor's own initial (Query="", filter=default) emission: every SearchViewModel in
        // the app graph is constructed eagerly at startup whether or not the user ever visits
        // Search, so without this every other screen's tests would also schedule a debounced
        // background search. The Search screen's own initial empty-state content is loaded once,
        // directly, right below instead.
        this.WhenAnyValue(x => x.Query, x => x.SelectedSourceOption)
            .Select(tuple => (tuple.Item1, tuple.Item2.Value))
            // DistinctUntilChanged BEFORE Skip(1): the constructor's initial emission must
            // establish the "last seen" baseline for the dedupe, otherwise the *next* identical
            // emission (e.g. a language switch that reassigns SelectedSourceOption) would be
            // treated as a change and spuriously re-run the search.
            .DistinctUntilChanged()
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Select(_ => Observable.FromAsync(RunLiveSearchAsync))
            .Switch()
            .Subscribe();

        // BUGFIX-2026-08: switching All/Official/AUR must not leave the previous filter's rows on
        // screen while the new search is in flight ("firefox lingered as AUR after switching
        // away"). Clear immediately and raise the loading indicator; the debounced pipeline above
        // then runs the actual search and repopulates. Value-based so a language switch (which
        // reassigns SelectedSourceOption to a new instance with the same value) is a no-op.
        this.WhenAnyValue(x => x.SelectedSourceOption)
            .Select(option => option.Value)
            .DistinctUntilChanged()
            .Skip(1)
            .Subscribe(_ =>
            {
                IsBusy = true;
                Results.Clear();
                this.RaisePropertyChanged(nameof(HasNoResults));
            });

        // UI-08: an empty query shows curated recommended packages instead of a blank screen —
        // loaded once immediately, matching the same fire-and-forget initial-load pattern
        // DashboardViewModel/InstalledPackagesViewModel already use in their own constructors.
        LoadRecommendedAsync().FireAndForget();
    }

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }

    public ReactiveCommand<PackageSummary, Unit> InstallCommand { get; }

    public ReactiveCommand<PackageSummary, Unit> SelectPackageCommand { get; }

    public ObservableCollection<PackageSummary> Results { get; } = [];

    public string PlaceholderLabel => Localization.GetString("Search.Placeholder");

    public string SearchButtonLabel => Localization.GetString("Search.Button");

    public string NoResultsLabel => Localization.GetString("Search.NoResults");

    public string InstallLabel => Localization.GetString("Package.Install");

    public string InstalledLabel => Localization.GetString("Package.StateInstalled");

    public bool HasNoResults => Results.Count == 0;

    public IReadOnlyList<SelectableOption<PackageSource?>> SourceOptions
    {
        get => _sourceOptions;
        private set => this.RaiseAndSetIfChanged(ref _sourceOptions, value);
    }

    public string Query
    {
        get => _query;
        set => this.RaiseAndSetIfChanged(ref _query, value);
    }

    public SelectableOption<PackageSource?> SelectedSourceOption
    {
        get => _selectedSourceOption;
        set => this.RaiseAndSetIfChanged(ref _selectedSourceOption, value);
    }

    public PackageSource? SourceFilter => SelectedSourceOption.Value;

    public PackageSummary? SelectedPackage
    {
        get => _selectedPackage;
        set => this.RaiseAndSetIfChanged(ref _selectedPackage, value);
    }

    public PackageDetailsViewModel? SelectedDetails
    {
        get => _selectedDetails;
        private set => this.RaiseAndSetIfChanged(ref _selectedDetails, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(PlaceholderLabel));
        this.RaisePropertyChanged(nameof(SearchButtonLabel));
        this.RaisePropertyChanged(nameof(NoResultsLabel));
        this.RaisePropertyChanged(nameof(InstallLabel));
        this.RaisePropertyChanged(nameof(InstalledLabel));

        var currentValue = SelectedSourceOption.Value;
        SourceOptions = BuildSourceOptions();
        SelectedSourceOption = SourceOptions.Single(option => option.Value == currentValue);
    }

    private IReadOnlyList<SelectableOption<PackageSource?>> BuildSourceOptions() =>
    [
        new(null, Localization.GetString("Search.SourceAll")),
        new(PackageSource.Official, Localization.GetString("Search.SourceOfficial")),
        new(PackageSource.Aur, Localization.GetString("Search.SourceAur")),
    ];

    private Task RunLiveSearchAsync() =>
        string.IsNullOrWhiteSpace(Query) ? LoadRecommendedAsync() : SearchAsync();

    /// <summary>BUGFIX-2026-08: keep the selected row selected across result reloads — the list is
    /// rebuilt on every live search, which otherwise drops the selection (and with it the detail
    /// pane). Re-selects by name if the previously selected package is still in the new set.</summary>
    private void RepopulateResults(IEnumerable<PackageSummary> packages)
    {
        var previouslySelectedName = SelectedPackage?.Name;

        Results.Clear();
        foreach (var package in packages)
        {
            Results.Add(package);
        }

        this.RaisePropertyChanged(nameof(HasNoResults));

        if (previouslySelectedName is not null)
        {
            var match = Results.FirstOrDefault(package =>
                package.Name.Equals(previouslySelectedName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedPackage = match;
            }
        }
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var results = await _backend.SearchAsync(Query, SourceFilter);
            RepopulateResults(results);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRecommendedAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var resultSets = await Task.WhenAll(
                RecommendedPackageNames.Select(name => _backend.SearchAsync(name, SourceFilter)));

            // Match each result set back to its recommended name (rather than just taking the
            // first entry, which is whatever the backend ranks first for that query and may not
            // be an exact-name match at all) so a fuzzy/AUR-heavy search result doesn't replace
            // the actual curated package.
            var curated = RecommendedPackageNames
                .Zip(resultSets, (name, set) => set.FirstOrDefault(
                    package => package.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                .Where(package => package is not null)
                .Select(package => package!)
                .ToArray();

            RepopulateResults(curated);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFromRowAsync(PackageSummary package)
    {
        SelectedPackage = package;
        if (SelectedDetails is { } details)
        {
            await details.InstallCommand.Execute();
            await SearchCommand.Execute();
        }
    }

    private void OnSelectedPackageChanged(PackageSummary? package)
    {
        if (package is null)
        {
            SelectedDetails = null;
            return;
        }

        var details = new PackageDetailsViewModel(
            _backend, package, Localization, _pkgbuildService, uninstallPolicy: _uninstallPolicy, notificationService: _notificationService);
        SelectedDetails = details;
        details.LoadAsync().FireAndForget();
    }
}
