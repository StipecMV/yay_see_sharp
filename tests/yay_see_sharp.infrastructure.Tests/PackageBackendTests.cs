using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using yay_see_sharp.infrastructure.Platform;
using Moq;

public class PackageBackendTests
{
    [Test]
    public async Task Demo_search_returns_realistic_official_and_aur_packages()
    {
        var backend = new DemoPackageBackend();

        var results = await backend.SearchAsync("", null);

        await Assert.That(results.Count).IsGreaterThan(3);
        await Assert.That(results.Any(package => package.Source == PackageSource.Official)).IsTrue();
        await Assert.That(results.Any(package => package.Source == PackageSource.Aur)).IsTrue();
        await Assert.That(results.Any(package => package.Name == "firefox" && package.IconUrl is not null)).IsTrue();
    }

    [Test]
    public async Task Yay_search_delegates_to_yay_and_filters_source()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var expected = new[]
        {
            new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled),
            new PackageSummary("hello-git", "2.12.1.r4", "Development package", PackageSource.Aur, 0, PackageState.NotInstalled),
        };
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "search output", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Ss", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));
        parser.Setup(item => item.ParseSearch("search output")).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var results = await backend.SearchAsync("hello", PackageSource.Aur);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results[0].Name).IsEqualTo("hello-git");
        parser.Verify(item => item.ParseSearch("search output"), Times.Once);
    }

    [Test]
    public async Task Yay_get_details_delegates_to_parser_when_yay_succeeds()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "info output", DateTimeOffset.UtcNow),
        };
        var expected = new PackageDetails(
            new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.Installed),
            null,
            null,
            [],
            []);
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Qi", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));
        parser.Setup(item => item.ParseInfo("info output")).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("hello");

        await Assert.That(details).IsEqualTo(expected);
    }

    [Test]
    public async Task Yay_get_details_returns_null_when_yay_fails()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.IsAny<CommandRequest>(),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("missing-package");

        await Assert.That(details).IsNull();
    }

    [Test]
    public async Task Yay_get_updates_delegates_to_parser()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "updates output", DateTimeOffset.UtcNow),
        };
        var expected = new[]
        {
            new UpdateInfo("hello", "2.12.1-1", "2.12.2-1", PackageSource.Official, 0),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Qu" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));
        parser.Setup(item => item.ParseUpdates("updates output")).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var updates = await backend.GetUpdatesAsync();

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Name).IsEqualTo("hello");
    }

    [Test]
    public async Task Yay_get_installed_packages_delegates_to_parser()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "installed output", DateTimeOffset.UtcNow),
        };
        var expected = new[]
        {
            new PackageSummary("hello", "2.12.1-1", string.Empty, PackageSource.Official, 0, PackageState.Installed),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Q" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));
        parser.Setup(item => item.ParseInstalled("installed output")).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var installed = await backend.GetInstalledPackagesAsync();

        await Assert.That(installed.Count).IsEqualTo(1);
        await Assert.That(installed[0].Name).IsEqualTo("hello");
    }

    [Test]
    public async Task Yay_installed_parser_reads_name_and_version_pairs()
    {
        var parser = new yay_see_sharp.infrastructure.Yay.YayOutputParser();
        const string output = "hello 2.12.1-1\nfirefox 128.0-1\n";

        var results = parser.ParseInstalled(output);

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Name).IsEqualTo("hello");
        await Assert.That(results[0].Version).IsEqualTo("2.12.1-1");
        await Assert.That(results[0].State).IsEqualTo(PackageState.Installed);
        await Assert.That(results[1].Name).IsEqualTo("firefox");
    }

    [Test]
    public async Task Demo_get_installed_packages_returns_only_installed_and_excludes_uninstalled()
    {
        var backend = new DemoPackageBackend();

        var installed = await backend.GetInstalledPackagesAsync();

        await Assert.That(installed.Count).IsGreaterThan(0);
        await Assert.That(installed.All(package => package.State != PackageState.NotInstalled)).IsTrue();
        await Assert.That(installed.Any(package => package.Name == "firefox")).IsTrue();
        await Assert.That(installed.Any(package => package.Name == "hello")).IsFalse();
    }

    [Test]
    public async Task Yay_get_statistics_counts_installed_packages()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "hello", DateTimeOffset.UtcNow),
            new CommandOutput(CommandOutputKind.StandardOutput, "firefox", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Qq" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.InstalledCount).IsEqualTo(2);
    }

    [Test]
    public async Task Yay_install_reports_completion_on_success()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "installing hello", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "--needed", "--noconfirm", "-S", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Install);
    }

    [Test]
    public async Task Yay_uninstall_removes_orphans_when_requested_and_reports_completion()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "removing hello", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "--noconfirm", "-Rns", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UninstallAsync("hello", removeOrphans: true))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Uninstall);
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "--noconfirm", "-Rns", "hello" })),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Yay_uninstall_keeps_orphans_when_not_requested_and_reports_completion()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "removing hello", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "--noconfirm", "-Rn", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UninstallAsync("hello", removeOrphans: false))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Kind).IsEqualTo(PackageOperationKind.Uninstall);
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "--noconfirm", "-Rn", "hello" })),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Not requested, so the real invocation must never carry the orphan-removal flag.
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.Contains("-Rns")),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_update_with_no_packages_runs_full_system_upgrade()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "upgrading system", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Syu", "--noconfirm" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UpdateAsync([]))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
    }

    [Test]
    public async Task Yay_update_with_specific_packages_targets_only_those_packages()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[]
        {
            new CommandOutput(CommandOutputKind.StandardOutput, "upgrading hello", DateTimeOffset.UtcNow),
        };
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-S", "--noconfirm", "--needed", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UpdateAsync(["hello"]))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-S", "--noconfirm", "--needed", "hello" })),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Yay_search_parser_reads_repository_name_version_and_description()
    {
        var parser = new yay_see_sharp.infrastructure.Yay.YayOutputParser();
        const string output = "core/hello 2.12.1-1\n    The classic GNU greeting utility.\naur/hello-git 2.12.1.r4.g123abc-1\n    Development version of the GNU greeting utility.\n";

        var results = parser.ParseSearch(output);

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Name).IsEqualTo("hello");
        await Assert.That(results[0].Version).IsEqualTo("2.12.1-1");
        await Assert.That(results[0].Source).IsEqualTo(PackageSource.Official);
        await Assert.That(results[0].Description).IsEqualTo("The classic GNU greeting utility.");
        await Assert.That(results[1].Source).IsEqualTo(PackageSource.Aur);
    }

    [Test]
    public async Task Yay_info_parser_reads_installed_package_fields()
    {
        var parser = new yay_see_sharp.infrastructure.Yay.YayOutputParser();
        const string output = "Name            : hello\n" +
            "Version         : 2.12.1-1\n" +
            "Description     : The classic GNU greeting utility.\n" +
            "Repository      : core\n" +
            "URL             : https://www.gnu.org/software/hello\n" +
            "Depends On      : glibc\n" +
            "Install Date    : Sat 01 Aug 2026 10:00:00 UTC\n" +
            "Packager        : Arch Linux\n" +
            "Installed Size  : 1.20 MiB\n";

        var details = parser.ParseInfo(output);

        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Summary.Name).IsEqualTo("hello");
        await Assert.That(details.Summary.Version).IsEqualTo("2.12.1-1");
        await Assert.That(details.Summary.State).IsEqualTo(PackageState.Installed);
        await Assert.That(details.Summary.Source).IsEqualTo(PackageSource.Official);
        await Assert.That(details.Summary.InstalledSizeBytes).IsEqualTo((long)(1.20 * 1024 * 1024));
        await Assert.That(details.Homepage).IsEqualTo("https://www.gnu.org/software/hello");
        await Assert.That(details.Dependencies.Count).IsEqualTo(1);
        await Assert.That(details.Dependencies[0].Name).IsEqualTo("glibc");
    }

    [Test]
    public async Task Yay_updates_parser_reads_current_and_available_versions()
    {
        var parser = new yay_see_sharp.infrastructure.Yay.YayOutputParser();
        const string output = "hello 2.12.1-1 -> 2.12.2-1\nfirefox 128.0-1 -> 129.0-1\n";

        var updates = parser.ParseUpdates(output);

        await Assert.That(updates.Count).IsEqualTo(2);
        await Assert.That(updates[0].Name).IsEqualTo("hello");
        await Assert.That(updates[0].CurrentVersion).IsEqualTo("2.12.1-1");
        await Assert.That(updates[0].AvailableVersion).IsEqualTo("2.12.2-1");
        await Assert.That(updates[1].Name).IsEqualTo("firefox");
    }

    [Test]
    public async Task Demo_install_then_uninstall_updates_package_state()
    {
        var backend = new DemoPackageBackend();

        await foreach (var _ in backend.InstallAsync("hello"))
        {
        }

        var installed = await backend.GetDetailsAsync("hello");
        await Assert.That(installed).IsNotNull();
        await Assert.That(installed!.Summary.State).IsEqualTo(PackageState.Installed);
        await Assert.That(installed.Files).Contains("/usr/bin/hello");

        await foreach (var _ in backend.UninstallAsync("hello", removeOrphans: true))
        {
        }

        var removed = await backend.GetDetailsAsync("hello");
        await Assert.That(removed).IsNotNull();
        await Assert.That(removed!.Summary.State).IsEqualTo(PackageState.NotInstalled);
    }

    [Test]
    public async Task Distribution_detector_uses_demo_mode_for_non_arch_distribution()
    {
        var osRelease = Path.GetTempFileName();
        await File.WriteAllTextAsync(osRelease, "ID=ubuntu\nPRETTY_NAME=Ubuntu Demo\n");
        try
        {
            var detector = new LinuxDistributionDetector(
                osRelease,
                new Dictionary<string, string>
                {
                    ["XDG_CURRENT_DESKTOP"] = "GNOME",
                    ["XDG_SESSION_TYPE"] = "wayland",
                });

            var snapshot = detector.Detect();
            var info = detector.CreateBackendInfo(snapshot);

            await Assert.That(snapshot.Id).IsEqualTo("ubuntu");
            await Assert.That(snapshot.DesktopEnvironment).IsEqualTo("GNOME");
            await Assert.That(snapshot.SessionType).IsEqualTo("wayland");
            await Assert.That(info.Mode).IsEqualTo(BackendMode.Demo);
            await Assert.That(info.PackageManager).IsEqualTo("demo");
        }
        finally
        {
            File.Delete(osRelease);
        }
    }

    [Test]
    public async Task Distribution_detector_selects_real_yay_mode_for_arch()
    {
        var osRelease = Path.GetTempFileName();
        await File.WriteAllTextAsync(osRelease, "ID=arch\nPRETTY_NAME=Arch Linux\n");
        try
        {
            var detector = new LinuxDistributionDetector(osRelease);
            var info = detector.CreateBackendInfo(detector.Detect());

            await Assert.That(info.Mode).IsEqualTo(BackendMode.Real);
            await Assert.That(info.PackageManager).IsEqualTo("yay");
            await Assert.That(info.IsSupported).IsTrue();
        }
        finally
        {
            File.Delete(osRelease);
        }
    }

    [Test]
    public async Task Yay_install_yields_cancelled_and_never_runs_the_command_when_the_auth_prompt_is_dismissed()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var privilegeService = new Mock<IPrivilegeService>();
        privilegeService.Setup(p => p.RequestElevationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PrivilegeResult.Cancelled);

        var backend = new YayPackageBackend(runner.Object, parser.Object, privilegeService: privilegeService.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_uninstall_yields_failed_and_never_runs_the_command_when_authentication_fails()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var privilegeService = new Mock<IPrivilegeService>();
        privilegeService.Setup(p => p.RequestElevationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PrivilegeResult.Failed);

        var backend = new YayPackageBackend(runner.Object, parser.Object, privilegeService: privilegeService.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UninstallAsync("hello", removeOrphans: true))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_update_runs_normally_when_elevation_is_granted()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var privilegeService = new Mock<IPrivilegeService>();
        privilegeService.Setup(p => p.RequestElevationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PrivilegeResult.Granted);
        var output = new[] { new CommandOutput(CommandOutputKind.StandardOutput, "upgrading system", DateTimeOffset.UtcNow) };
        runner.Setup(item => item.RunAsync(
                It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object, privilegeService: privilegeService.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UpdateAsync([]))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        privilegeService.Verify(p => p.RequestElevationAsync(It.IsAny<CancellationToken>()), Times.Once);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Yay_install_skips_elevation_entirely_when_no_privilege_service_is_configured()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var output = new[] { new CommandOutput(CommandOutputKind.StandardOutput, "installing hello", DateTimeOffset.UtcNow) };
        runner.Setup(item => item.RunAsync(
                It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, output, false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
    }
}
