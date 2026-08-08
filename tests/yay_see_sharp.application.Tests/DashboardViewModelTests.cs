using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class DashboardViewModelTests
{
    [Test]
    public async Task Constructor_loads_statistics_and_updates_automatically_without_refresh_click()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));

        await Assert.That(viewModel.Statistics).IsNotNull();
        await Assert.That(viewModel.Statistics!.InstalledCount).IsGreaterThan(0);
    }

    [Test]
    public async Task Refresh_loads_statistics_and_updates_from_backend()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));

        await viewModel.RefreshCommand.Execute();

        await Assert.That(viewModel.Statistics).IsNotNull();
        await Assert.That(viewModel.IsBusy).IsFalse();
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }

    [Test]
    public async Task Switching_language_live_updates_dashboard_labels()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, localization);

        await Assert.That(viewModel.RefreshLabel).IsEqualTo("Refresh");
        await Assert.That(viewModel.InstalledLabel).IsEqualTo("Installed");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.RefreshLabel).IsEqualTo("Obnoviť");
        await Assert.That(viewModel.InstalledLabel).IsEqualTo("Nainštalované");
    }

    [Test]
    public async Task Startup_load_reports_firefox_as_having_an_available_update()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));

        await Assert.That(viewModel.HasNoUpdates).IsFalse();
        await Assert.That(viewModel.UpdateItems.Any(item => item.Info.Name == "firefox")).IsTrue();
    }

    [Test]
    public async Task UpdateAllCommand_updates_all_outdated_packages_and_clears_the_update_list()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));

        await viewModel.UpdateAllCommand.Execute();

        await Assert.That(viewModel.UpdateOperation).IsNull();
        await Assert.That(viewModel.HasNoUpdates).IsTrue();
    }

    [Test]
    public async Task UpdatePackageCommand_updates_a_single_package_by_name()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));

        await viewModel.UpdatePackageCommand.Execute("firefox");

        await Assert.That(viewModel.UpdateOperation).IsNull();
        await Assert.That(viewModel.UpdateItems.Any(item => item.Info.Name == "firefox")).IsFalse();
    }

    [Test]
    public async Task Update_item_command_invokes_the_shared_update_package_command()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, new LocalizationService("en"));
        var firefoxItem = viewModel.UpdateItems.Single(item => item.Info.Name == "firefox");

        await firefoxItem.UpdateCommand.Execute("firefox");

        await Assert.That(viewModel.UpdateItems.Any(item => item.Info.Name == "firefox")).IsFalse();
    }

    [Test]
    public async Task Switching_language_live_updates_update_labels()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new DashboardViewModel(backend, localization);
        var firefoxItem = viewModel.UpdateItems.Single(item => item.Info.Name == "firefox");

        await Assert.That(viewModel.UpdateAllLabel).IsEqualTo("Update all");
        await Assert.That(firefoxItem.UpdateLabel).IsEqualTo("Update");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.UpdateAllLabel).IsEqualTo("Aktualizovať všetko");
        await Assert.That(firefoxItem.UpdateLabel).IsEqualTo("Aktualizovať");
    }

    private static Mock<IPackageBackend> CreateBackendLastSyncedMinutesAgo(int minutes)
    {
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageStatistics(0, 0, 0, 0, 0, 0, 0, DateTimeOffset.UtcNow.AddMinutes(-minutes)));
        backend.Setup(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<UpdateInfo>)[]);
        return backend;
    }

    [Test]
    public async Task Last_synced_label_reports_a_localized_relative_time_in_english()
    {
        var backend = CreateBackendLastSyncedMinutesAgo(5);
        var viewModel = new DashboardViewModel(backend.Object, new LocalizationService("en"));
        await viewModel.InitialLoadTask;

        await Assert.That(viewModel.LastSyncedLabel).IsEqualTo("Last synced 5 minutes ago");
    }

    [Test]
    public async Task Last_synced_label_reports_a_localized_relative_time_in_slovak()
    {
        var backend = CreateBackendLastSyncedMinutesAgo(5);
        var viewModel = new DashboardViewModel(backend.Object, new LocalizationService("sk"));
        await viewModel.InitialLoadTask;

        await Assert.That(viewModel.LastSyncedLabel).IsEqualTo("Naposledy synchronizované pred 5 minútami");
    }

    [Test]
    public async Task Last_synced_label_uses_the_singular_form_for_exactly_one_minute()
    {
        var backend = CreateBackendLastSyncedMinutesAgo(1);
        var viewModel = new DashboardViewModel(backend.Object, new LocalizationService("en"));
        await viewModel.InitialLoadTask;

        await Assert.That(viewModel.LastSyncedLabel).IsEqualTo("Last synced 1 minute ago");
    }

    [Test]
    public async Task Last_synced_label_reports_moments_ago_for_a_sub_minute_gap()
    {
        var backend = CreateBackendLastSyncedMinutesAgo(0);
        var viewModel = new DashboardViewModel(backend.Object, new LocalizationService("en"));
        await viewModel.InitialLoadTask;

        await Assert.That(viewModel.LastSyncedLabel).IsEqualTo("Last synced moments ago");
    }

    // BUGFIX-2026-08: the "Updates available" card must mirror the update list actually rendered
    // below it. GetStatisticsAsync counts `pacman -Qu` (repo-only); the list comes from `yay -Qu`
    // (repo + AUR). The dashboard overrides the statistic with the list's own count.
    [Test]
    public async Task Updates_available_statistic_matches_the_rendered_update_list()
    {
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PackageStatistics(
                InstalledCount: 130,
                ExplicitCount: 130,
                DependencyCount: 0,
                AurCount: 20,
                UpdatesAvailable: 0, // pacman -Qu says none — but yay -Qu below finds three
                InstalledSizeBytes: 0,
                OrphanCount: 0,
                LastUpdateCheck: DateTimeOffset.UtcNow));
        backend.Setup(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new UpdateInfo("firefox", "137.0.2-1", "137.0.3-1", PackageSource.Official, 0),
                new UpdateInfo("vlc", "3.0.20-1", "3.0.21-1", PackageSource.Official, 0),
                new UpdateInfo("hello-git", "1.0-1", "1.1-1", PackageSource.Aur, 0),
            });

        var viewModel = new DashboardViewModel(backend.Object, new LocalizationService("en"));
        await viewModel.InitialLoadTask;

        await Assert.That(viewModel.Statistics).IsNotNull();
        await Assert.That(viewModel.Statistics!.UpdatesAvailable).IsEqualTo(3);
        await Assert.That(viewModel.Updates.Count).IsEqualTo(3);
    }
}
