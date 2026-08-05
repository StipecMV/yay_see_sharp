using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class InstalledPackagesViewModel : LocalizedViewModelBase
{
    private readonly IPackageBackend _backend;
    private readonly IUninstallPolicy? _uninstallPolicy;
    private readonly INotificationService? _notificationService;
    private PackageSummary? _selectedPackage;
    private PackageDetailsViewModel? _selectedDetails;
    private BuildJobViewModel? _buildJob;
    private bool _isBusy;
    private string? _errorMessage;

    public InstalledPackagesViewModel(
        IPackageBackend backend,
        ILocalizationService localization,
        IUninstallPolicy? uninstallPolicy = null,
        INotificationService? notificationService = null)
        : base(localization)
    {
        _backend = backend;
        _uninstallPolicy = uninstallPolicy;
        _notificationService = notificationService;
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        this.WhenAnyValue(x => x.SelectedPackage).Subscribe(OnSelectedPackageChanged);
        this.WhenAnyValue(x => x.SelectedDetails)
            .Select(details => details is null
                ? Observable.Return<OperationViewModel?>(null)
                : details.WhenAnyValue(d => d.Operation))
            .Switch()
            .Subscribe(operation => BuildJob = operation is null || SelectedDetails is null
                ? null
                : new BuildJobViewModel(SelectedDetails.Summary.Name, operation, Localization));
        RefreshAsync().FireAndForget();
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ObservableCollection<PackageSummary> Packages { get; } = [];

    public string RefreshLabel => Localization.GetString("Dashboard.Refresh");

    public string EmptyLabel => Localization.GetString("Installed.Empty");

    public bool HasNoPackages => Packages.Count == 0;

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
    }

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

    private void OnSelectedPackageChanged(PackageSummary? package)
    {
        if (package is null)
        {
            SelectedDetails = null;
            return;
        }

        var details = new PackageDetailsViewModel(
            _backend, package, Localization, uninstallPolicy: _uninstallPolicy, notificationService: _notificationService);
        SelectedDetails = details;
        details.LoadAsync().FireAndForget();
    }
}
