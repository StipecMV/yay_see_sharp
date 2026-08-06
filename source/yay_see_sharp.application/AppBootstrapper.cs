using yay_see_sharp.application.Platform;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure;
using yay_see_sharp.infrastructure.Filesystem;
using yay_see_sharp.infrastructure.Http;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Notifications;
using yay_see_sharp.infrastructure.Platform;
using yay_see_sharp.infrastructure.Privilege;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Scheduling;
using yay_see_sharp.infrastructure.Settings;
using yay_see_sharp.infrastructure.Yay;

namespace yay_see_sharp.application;

public sealed class AppComposition
{
    public required MainWindowViewModel ViewModel { get; init; }

    public required ISingleInstanceService SingleInstanceService { get; init; }

    public required UpdateScheduler UpdateScheduler { get; init; }

    public required AvaloniaTrayService TrayService { get; init; }
}

/// <summary>
/// The application's composition root: the one place real infrastructure services (sudo
/// elevation, the settings file, the notification sender, the update scheduler, ...) get
/// constructed and wired into the ViewModel tree. App.axaml.cs only handles Avalonia lifecycle
/// (window/tray/theme wiring); MainWindowViewModel and its child ViewModels take everything via
/// constructor, so neither owns this construction and both stay trivial to unit test.
/// </summary>
public static class AppBootstrapper
{
    /// <summary>Returns null when this process should not start a second instance (see <see cref="ISingleInstanceService"/>).</summary>
    public static AppComposition? Bootstrap()
    {
        var singleInstanceService = new FileLockSingleInstanceService();
        if (!singleInstanceService.TryAcquire())
        {
            // Best-effort: ask the already-running instance to restore/focus itself before this
            // second launch exits. A false return (nothing listening) is not itself an error —
            // this process still must not start a second session either way.
            singleInstanceService.TryActivateExisting();
            return null;
        }

        var settingsStore = new FileSettingsStore();
        var settings = Task.Run(() => settingsStore.LoadAsync()).GetAwaiter().GetResult();
        var localizationService = new LocalizationService(settings.Language);

        // Every concrete Infrastructure service a ViewModel needs is constructed here, once, and
        // handed down via constructor — ViewModels themselves never `new` one up as a hidden
        // fallback (that used to make production behavior depend on a default parameter no one
        // was looking at).
        var engineDetector = new EngineDetector();
        var folderBrowserService = new FolderBrowserService();
        var pkgbuildService = new PkgbuildService();

        // Built before the backend factory (which needs it as the live IBuildDirectoryPolicy) and
        // before the other child screens (as the live IUninstallPolicy / INotificationSettings),
        // so everything reads from the same in-memory instance the Settings screen edits, not a
        // stale reload from disk.
        var settingsViewModel = new SettingsViewModel(
            settingsStore, localizationService, settings, engineDetector, folderBrowserService);

        // Constructed before MainWindowViewModel exists (the backend needs it earlier than the
        // window does) — its password prompt is wired to the real UI once the view model exists.
        var privilegeService = new SudoPrivilegeService();
        var backendFactory = new PackageBackendFactory(
            new LinuxDistributionDetector(),
            engineDetector,
            new SystemCommandRunner(),
            new YayOutputParser(),
            privilegeService,
            settingsViewModel);
        var backend = backendFactory.Create();

        var notificationService = new SettingsAwareNotificationService(
            new NotifySendNotificationService(new SystemCommandRunner()), settingsViewModel);

        var dashboard = new DashboardViewModel(backend, localizationService, notificationService);
        var search = new SearchViewModel(backend, localizationService, pkgbuildService, settingsViewModel, notificationService);
        var installed = new InstalledPackagesViewModel(backend, localizationService, pkgbuildService, settingsViewModel, notificationService);

        // Only constructed when there's actually a missing backend to offer installing — on Real
        // or Demo mode there's nothing for it to do.
        IBackendInstaller? backendInstaller = backend.Info.Mode == BackendMode.Unavailable
            ? new YayBackendInstaller(new SystemCommandRunner(), backend.Info.DistributionId == "cachyos", privilegeService)
            : null;

        var viewModel = new MainWindowViewModel(
            backend, localizationService, settingsViewModel, dashboard, search, installed, backendInstaller);
        privilegeService.PasswordPrompt = _ => viewModel.RequestAuthenticationAsync();

        // Always started: the loop itself re-checks Settings.AutoUpdateCheckEnabled every poll
        // tick and no-ops while it's off, so this reacts immediately if the user enables it
        // mid-session rather than only taking effect after a restart.
        var updateScheduler = new UpdateScheduler(new SystemClock(), backend, settingsViewModel)
        {
            OnUpdatesFound = updates => dashboard.NotifyUpdatesAvailableAsync(updates.Count),
        };

        return new AppComposition
        {
            ViewModel = viewModel,
            SingleInstanceService = singleInstanceService,
            UpdateScheduler = updateScheduler,
            TrayService = new AvaloniaTrayService(localizationService),
        };
    }
}
