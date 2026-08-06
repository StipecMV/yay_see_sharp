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
        this.WhenAnyValue(x => x.SelectedPackage).Subscribe(OnSelectedPackageChanged);
    }

    public ReactiveCommand<Unit, Unit> SearchCommand { get; }

    public ReactiveCommand<PackageSummary, Unit> InstallCommand { get; }

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

    private async Task SearchAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var results = await _backend.SearchAsync(Query, SourceFilter);
            Results.Clear();
            foreach (var package in results)
            {
                Results.Add(package);
            }

            this.RaisePropertyChanged(nameof(HasNoResults));
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
