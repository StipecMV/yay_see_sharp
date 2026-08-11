using System.Linq;
using System.Reactive;
using log4net;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class DashboardViewModel : LocalizedViewModelBase
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DashboardViewModel));
    private readonly IPackageBackend _backend;
    private readonly INotificationService _notificationService;
    private PackageStatistics? _statistics;
    private IReadOnlyList<UpdateInfo> _updates = [];
    private IReadOnlyList<UpdateItemViewModel> _updateItems = [];
    private OperationViewModel? _updateOperation;
    private string? _notificationMessage;
    private bool _isBusy;
    private string? _errorMessage;

    public DashboardViewModel(IPackageBackend backend, ILocalizationService localization, INotificationService? notificationService = null)
        : base(localization)
    {
        _backend = backend;
        _notificationService = notificationService ?? NullNotificationService.Instance;
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        UpdateAllCommand = ReactiveCommand.CreateFromTask(() => RunUpdateAsync([]));
        UpdatePackageCommand = ReactiveCommand.CreateFromTask<string>(name => RunUpdateAsync([name]));
        SelectPackageCommand = ReactiveCommand.Create<string>(name => PackageActivated?.Invoke(this, name));
        DismissNotificationCommand = ReactiveCommand.Create(() => { NotificationMessage = null; });
        InitialLoadTask = RefreshAsync();
    }

    /// <summary>Completes once the constructor-triggered initial refresh finishes; used to gate the splash screen.</summary>
    public Task InitialLoadTask { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public ReactiveCommand<Unit, Unit> UpdateAllCommand { get; }

    public ReactiveCommand<string, Unit> UpdatePackageCommand { get; }

    public ReactiveCommand<string, Unit> SelectPackageCommand { get; }

    public ReactiveCommand<Unit, Unit> DismissNotificationCommand { get; }

    /// <summary>UI-05: raised when the user clicks an update row (not the Update button) — MainWindowViewModel navigates to Installed and selects this package's detail.</summary>
    public event EventHandler<string>? PackageActivated;

    public string RefreshLabel => Localization.GetString("Dashboard.Refresh");

    public string WelcomeLabel => Localization.GetString("Dashboard.Welcome");

    public string LastSyncedLabel => Statistics?.LastUpdateCheck is { } lastSync
        ? string.Format(Localization.GetString("Dashboard.LastSynced"), FormatRelative(DateTimeOffset.UtcNow - lastSync))
        : Localization.GetString("Dashboard.LastSyncedNever");

    public string StatisticsLabel => Localization.GetString("Dashboard.Statistics");

    public string UpdatesLabel => Localization.GetString("Dashboard.Updates");

    public string NoUpdatesLabel => Localization.GetString("Dashboard.NoUpdates");

    public string UpdateAllLabel => Localization.GetString("Dashboard.UpdateAll");

    public string InstalledLabel => Localization.GetString("Dashboard.Installed");

    public string ExplicitLabel => Localization.GetString("Dashboard.Explicit");

    public string DependenciesLabel => Localization.GetString("Dashboard.Dependencies");

    public string AurLabel => Localization.GetString("Dashboard.Aur");

    public string UpdatesAvailableLabel => Localization.GetString("Dashboard.UpdatesAvailable");

    public string OrphansLabel => Localization.GetString("Dashboard.Orphans");

    public PackageStatistics? Statistics
    {
        get => _statistics;
        private set
        {
            this.RaiseAndSetIfChanged(ref _statistics, value);
            this.RaisePropertyChanged(nameof(LastSyncedLabel));
        }
    }

    public IReadOnlyList<UpdateInfo> Updates
    {
        get => _updates;
        private set
        {
            this.RaiseAndSetIfChanged(ref _updates, value);
            this.RaisePropertyChanged(nameof(HasNoUpdates));
            UpdateItems = value.Select(info => new UpdateItemViewModel(info, UpdatePackageCommand, SelectPackageCommand, Localization)).ToArray();
        }
    }

    public IReadOnlyList<UpdateItemViewModel> UpdateItems
    {
        get => _updateItems;
        private set => this.RaiseAndSetIfChanged(ref _updateItems, value);
    }

    public OperationViewModel? UpdateOperation
    {
        get => _updateOperation;
        private set => this.RaiseAndSetIfChanged(ref _updateOperation, value);
    }

    public bool HasNoUpdates => Updates.Count == 0;

    public string? NotificationMessage
    {
        get => _notificationMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _notificationMessage, value);
            this.RaisePropertyChanged(nameof(HasNotification));
        }
    }

    public bool HasNotification => NotificationMessage is not null;

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(DisplayErrorMessage));
        }
    }

    public string? DisplayErrorMessage => ErrorMessage is null
        ? null
        : $"{Localization.GetString("Generic.ErrorPrefix")}: {ErrorMessage}";

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(RefreshLabel));
        this.RaisePropertyChanged(nameof(WelcomeLabel));
        this.RaisePropertyChanged(nameof(LastSyncedLabel));
        this.RaisePropertyChanged(nameof(StatisticsLabel));
        this.RaisePropertyChanged(nameof(UpdatesLabel));
        this.RaisePropertyChanged(nameof(NoUpdatesLabel));
        this.RaisePropertyChanged(nameof(UpdateAllLabel));
        this.RaisePropertyChanged(nameof(InstalledLabel));
        this.RaisePropertyChanged(nameof(ExplicitLabel));
        this.RaisePropertyChanged(nameof(DependenciesLabel));
        this.RaisePropertyChanged(nameof(AurLabel));
        this.RaisePropertyChanged(nameof(UpdatesAvailableLabel));
        this.RaisePropertyChanged(nameof(OrphansLabel));
        this.RaisePropertyChanged(nameof(DisplayErrorMessage));
    }

    /// <summary>Public so the shell (MainWindowViewModel) can refresh the dashboard when the user
    /// navigates to it after package operations elsewhere.</summary>
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var statistics = await _backend.GetStatisticsAsync();
            Updates = await _backend.GetUpdatesAsync();
            // BUGFIX-2026-08: the "Updates available" card must agree with the update list below
            // it. GetStatisticsAsync counts `pacman -Qu` (repo-only), while the list comes from
            // `yay -Qu` (repo + AUR) — on a system with AUR packages the two disagreed. The
            // count now always mirrors the list that's actually rendered.
            Statistics = statistics with { UpdatesAvailable = Updates.Count };
            Log.Info($"Dashboard refreshed: installed={statistics.InstalledCount} aur={statistics.AurCount?.ToString() ?? "?"} updates={Updates.Count}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Error("Dashboard refresh failed", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Entry point for the background IUpdateScheduler: surfaces newly-found scheduled updates through the same banner RunUpdateAsync uses, and reloads the list so the dashboard reflects them.</summary>
    public async Task NotifyUpdatesAvailableAsync(int count)
    {
        var message = string.Format(Localization.GetString("Dashboard.ScheduledUpdatesFound"), count);
        NotificationMessage = message;
        await _notificationService.SendAsync(Localization.GetString("Notification.UpdatesAvailable"), message, NotificationLevel.Info);
        await RefreshAsync();
    }

    private async Task RunUpdateAsync(IReadOnlyCollection<string> packageNames)
    {
        var operation = new OperationViewModel(PackageOperationKind.Update, Localization);
        UpdateOperation = operation;

        try
        {
            await foreach (var progress in _backend.UpdateAsync(packageNames, operation.CancellationToken))
            {
                operation.Apply(progress);
            }

            if (operation.Stage == PackageOperationStage.Completed)
            {
                NotificationMessage = operation.Message;
                await _notificationService.SendAsync(
                    Localization.GetString("Notification.UpdateCompleted"), operation.Message, NotificationLevel.Success);
            }
            else if (operation.Stage == PackageOperationStage.Failed)
            {
                await _notificationService.SendAsync(
                    Localization.GetString("Notification.OperationFailed"), operation.Message, NotificationLevel.Error);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _notificationService.SendAsync(
                Localization.GetString("Notification.OperationFailed"), ex.Message, NotificationLevel.Error);
        }
        finally
        {
            UpdateOperation = null;
        }
    }

    private string FormatRelative(TimeSpan elapsed)
    {
        if (elapsed.TotalMinutes < 1)
        {
            return Localization.GetString("Dashboard.Relative.Moments");
        }

        if (elapsed.TotalHours < 1)
        {
            return FormatUnit((int)elapsed.TotalMinutes, "Dashboard.Relative.Minute", "Dashboard.Relative.Minutes");
        }

        if (elapsed.TotalDays < 1)
        {
            return FormatUnit((int)elapsed.TotalHours, "Dashboard.Relative.Hour", "Dashboard.Relative.Hours");
        }

        return FormatUnit((int)elapsed.TotalDays, "Dashboard.Relative.Day", "Dashboard.Relative.Days");
    }

    private string FormatUnit(int count, string singularKey, string pluralKey) =>
        $"{count} {Localization.GetString(count == 1 ? singularKey : pluralKey)}";
}
