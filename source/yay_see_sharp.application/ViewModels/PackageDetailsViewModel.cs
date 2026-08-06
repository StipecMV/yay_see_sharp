using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class PackageDetailsViewModel : LocalizedViewModelBase
{
    private readonly IPackageBackend _backend;
    private readonly IPkgbuildService _pkgbuildService;
    private readonly IUninstallPolicy _uninstallPolicy;
    private readonly INotificationService _notificationService;
    private PackageDetails? _details;
    private OperationViewModel? _operation;
    private PkgbuildViewModel? _pkgbuild;
    private bool _isBusy;
    private string? _errorMessage;

    public PackageDetailsViewModel(
        IPackageBackend backend,
        PackageSummary summary,
        ILocalizationService localization,
        IPkgbuildService pkgbuildService,
        IUninstallPolicy? uninstallPolicy = null,
        INotificationService? notificationService = null)
        : base(localization)
    {
        _backend = backend;
        _pkgbuildService = pkgbuildService;
        _uninstallPolicy = uninstallPolicy ?? DefaultUninstallPolicy.Instance;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        Summary = summary;

        var state = this.WhenAnyValue(x => x.Details).Select(details => details?.Summary.State);
        var canInstall = state.Select(value => value is null or PackageState.NotInstalled);
        var canUninstall = state.Select(value => value is PackageState.Installed or PackageState.UpdateAvailable);

        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync, canInstall);
        UninstallCommand = ReactiveCommand.CreateFromTask(UninstallAsync, canUninstall);
        ViewPkgbuildCommand = ReactiveCommand.CreateFromTask(ShowPkgbuildAsync);
    }

    public PackageSummary Summary { get; }

    public ReactiveCommand<Unit, Unit> InstallCommand { get; }

    public ReactiveCommand<Unit, Unit> UninstallCommand { get; }

    public ReactiveCommand<Unit, Unit> ViewPkgbuildCommand { get; }

    public string InstallLabel => Localization.GetString("Package.Install");

    public string UninstallLabel => Localization.GetString("Package.Uninstall");

    public string HomepageLabel => Localization.GetString("Package.Homepage");

    public string NoDetailsLabel => Localization.GetString("Package.NoDetails");

    public string ViewPkgbuildLabel => Localization.GetString("Package.ViewPkgbuild");

    public string MaintainerLabel => Localization.GetString("Package.Maintainer");

    public string VotesLabel => Localization.GetString("Package.Votes");

    public string InstallSizeLabel => Localization.GetString("Package.InstallSize");

    public string DependenciesLabel => Localization.GetString("Package.Dependencies");

    public string StateLabel => Localization.GetString(Details?.Summary.State switch
    {
        PackageState.Installed => "Package.StateInstalled",
        PackageState.UpdateAvailable => "Package.StateUpdateAvailable",
        _ => "Package.StateNotInstalled",
    });

    public bool IsInstalled => (Details?.Summary.State ?? Summary.State) is PackageState.Installed or PackageState.UpdateAvailable;

    public bool HasDependencies => Details?.Dependencies.Count > 0;

    public PackageDetails? Details
    {
        get => _details;
        private set
        {
            this.RaiseAndSetIfChanged(ref _details, value);
            this.RaisePropertyChanged(nameof(StateLabel));
            this.RaisePropertyChanged(nameof(IsInstalled));
            this.RaisePropertyChanged(nameof(HasDependencies));
        }
    }

    public OperationViewModel? Operation
    {
        get => _operation;
        private set => this.RaiseAndSetIfChanged(ref _operation, value);
    }

    public PkgbuildViewModel? Pkgbuild
    {
        get => _pkgbuild;
        private set => this.RaiseAndSetIfChanged(ref _pkgbuild, value);
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
        this.RaisePropertyChanged(nameof(InstallLabel));
        this.RaisePropertyChanged(nameof(UninstallLabel));
        this.RaisePropertyChanged(nameof(HomepageLabel));
        this.RaisePropertyChanged(nameof(NoDetailsLabel));
        this.RaisePropertyChanged(nameof(ViewPkgbuildLabel));
        this.RaisePropertyChanged(nameof(MaintainerLabel));
        this.RaisePropertyChanged(nameof(VotesLabel));
        this.RaisePropertyChanged(nameof(InstallSizeLabel));
        this.RaisePropertyChanged(nameof(DependenciesLabel));
        this.RaisePropertyChanged(nameof(StateLabel));
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Details = await _backend.GetDetailsAsync(Summary.Name);
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

    private async Task InstallAsync()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, Localization);
        Operation = operation;

        try
        {
            await foreach (var progress in _backend.InstallAsync(Summary.Name, operation.CancellationToken))
            {
                operation.Apply(progress);
            }

            await NotifyOperationOutcomeAsync(operation, "Notification.InstallSucceeded");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _notificationService.SendAsync(
                Localization.GetString("Notification.OperationFailed"), ex.Message, NotificationLevel.Error);
        }
        finally
        {
            Operation = null;
        }
    }

    private async Task UninstallAsync()
    {
        var operation = new OperationViewModel(PackageOperationKind.Uninstall, Localization);
        Operation = operation;

        try
        {
            await foreach (var progress in _backend.UninstallAsync(
                Summary.Name, _uninstallPolicy.RemoveOrphansByDefault, operation.CancellationToken))
            {
                operation.Apply(progress);
            }

            await NotifyOperationOutcomeAsync(operation, "Notification.UninstallSucceeded");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _notificationService.SendAsync(
                Localization.GetString("Notification.OperationFailed"), ex.Message, NotificationLevel.Error);
        }
        finally
        {
            Operation = null;
        }
    }

    private async Task NotifyOperationOutcomeAsync(OperationViewModel operation, string successKey)
    {
        if (operation.Stage == PackageOperationStage.Completed)
        {
            await _notificationService.SendAsync(Localization.GetString(successKey), Summary.Name, NotificationLevel.Success);
        }
        else if (operation.Stage == PackageOperationStage.Failed)
        {
            await _notificationService.SendAsync(
                Localization.GetString("Notification.OperationFailed"), operation.Message, NotificationLevel.Error);
        }
    }

    private async Task ShowPkgbuildAsync()
    {
        var pkgbuild = new PkgbuildViewModel(Summary.Name, Summary.Source, _pkgbuildService, Localization);
        Pkgbuild = pkgbuild;
        pkgbuild.LoadAsync().FireAndForget();
        await pkgbuild.WaitForCloseAsync();
        Pkgbuild = null;
    }

    /// <summary>Matches AppSettings.Default.RemoveOrphansByDefault so callers that don't wire a real policy (mainly tests) keep prior behavior.</summary>
    private sealed class DefaultUninstallPolicy : IUninstallPolicy
    {
        public static readonly DefaultUninstallPolicy Instance = new();

        public bool RemoveOrphansByDefault => true;
    }
}
