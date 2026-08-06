using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class InstalledPackagesViewModel : LocalizedViewModelBase
{
    private readonly IPackageBackend _backend;
    private readonly IPkgbuildService _pkgbuildService;
    private readonly IUninstallPolicy? _uninstallPolicy;
    private readonly INotificationService? _notificationService;
    private PackageSummary? _selectedPackage;
    private PackageDetailsViewModel? _selectedDetails;
    private BuildJobViewModel? _buildJob;
    private bool _isBusy;
    private string? _errorMessage;
    private string _query = string.Empty;
    private SelectableOption<PackageSource?> _selectedSourceOption;
    private IReadOnlyList<SelectableOption<PackageSource?>> _sourceOptions;

    public InstalledPackagesViewModel(
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
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        this.WhenAnyValue(x => x.SelectedPackage).Subscribe(OnSelectedPackageChanged);

        // All/Official/AUR filter + live search — purely local (Packages is already fully loaded
        // client-side), so there's no backend round-trip to debounce for cost reasons, but the
        // 300ms debounce is kept anyway so typing feels identical to the Search screen.
        this.WhenAnyValue(x => x.Query, x => x.SourceFilter)
            .Throttle(TimeSpan.FromMilliseconds(300), RxApp.MainThreadScheduler)
            .Subscribe(_ => ApplyFilter());
        this.WhenAnyValue(x => x.SelectedDetails)
            .Select(details => details is null
                ? Observable.Return<OperationViewModel?>(null)
                : details.WhenAnyValue(d => d.Operation))
            .Switch()
            .Subscribe(operation => BuildJob = operation is null || SelectedDetails is null
                ? null
                : new BuildJobViewModel(SelectedDetails.Summary.Name, operation, Localization));

        // UI-04/UI-11/UI-13: auto-refresh the list once an install/uninstall on the selected
        // package's detail pane completes — no manual Refresh button needed anymore.
        this.WhenAnyValue(x => x.SelectedDetails)
            .Select(details => details is null
                ? Observable.Empty<PackageOperationStage>()
                : details.WhenAnyValue(d => d.Operation)
                    .Select(operation => operation is null
                        ? Observable.Empty<PackageOperationStage>()
                        : operation.WhenAnyValue(o => o.Stage))
                    .Switch())
            .Switch()
            .Where(stage => stage == PackageOperationStage.Completed)
            .Subscribe(_ => RefreshAsync().FireAndForget());

        RefreshAsync().FireAndForget();
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>The full, unfiltered set loaded from the backend — bind to <see cref="FilteredPackages"/> for display.</summary>
    public ObservableCollection<PackageSummary> Packages { get; } = [];

    /// <summary>All/Official/AUR + name search applied to <see cref="Packages"/> — this is what the list view binds to.</summary>
    public ObservableCollection<PackageSummary> FilteredPackages { get; } = [];

    public string RefreshLabel => Localization.GetString("Dashboard.Refresh");

    public string EmptyLabel => Localization.GetString("Installed.Empty");

    public string NoResultsLabel => Localization.GetString("Search.NoResults");

    public string PlaceholderLabel => Localization.GetString("Search.Placeholder");

    public bool HasNoPackages => Packages.Count == 0;

    /// <summary>Distinct from <see cref="HasNoPackages"/>: nothing installed vs. nothing matches the current filter/search.</summary>
    public bool HasNoFilteredResults => Packages.Count > 0 && FilteredPackages.Count == 0;

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

    public BuildJobViewModel? BuildJob
    {
        get => _buildJob;
        private set => this.RaiseAndSetIfChanged(ref _buildJob, value);
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
        this.RaisePropertyChanged(nameof(RefreshLabel));
        this.RaisePropertyChanged(nameof(EmptyLabel));
        this.RaisePropertyChanged(nameof(NoResultsLabel));
        this.RaisePropertyChanged(nameof(PlaceholderLabel));

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

    private async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var installed = await _backend.GetInstalledPackagesAsync();
            Packages.Clear();
            foreach (var package in installed)
            {
                Packages.Add(package);
            }

            this.RaisePropertyChanged(nameof(HasNoPackages));
            ApplyFilter();
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

    /// <summary>All/Official/AUR + name search over the already-loaded <see cref="Packages"/> — purely local, no backend call.</summary>
    private void ApplyFilter()
    {
        var query = Query.Trim();
        var filtered = Packages.Where(package =>
            (SourceFilter is null || package.Source == SourceFilter) &&
            (query.Length == 0 || package.Name.Contains(query, StringComparison.OrdinalIgnoreCase)));

        FilteredPackages.Clear();
        foreach (var package in filtered)
        {
            FilteredPackages.Add(package);
        }

        this.RaisePropertyChanged(nameof(HasNoFilteredResults));
    }

    /// <summary>UI-05: navigated to from the Dashboard's update list — selects the matching already-loaded installed package, if present, so its detail pane shows.</summary>
    public void SelectByName(string name)
    {
        var package = Packages.FirstOrDefault(p => p.Name == name);
        if (package is not null)
        {
            // Reset any active filter/search first (synchronously, not via the debounced
            // pipeline) — otherwise the target package could be hidden from FilteredPackages,
            // which the list view actually binds to, and selecting it would silently fail.
            _query = string.Empty;
            this.RaisePropertyChanged(nameof(Query));
            _selectedSourceOption = SourceOptions[0];
            this.RaisePropertyChanged(nameof(SelectedSourceOption));
            ApplyFilter();

            SelectedPackage = package;
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
