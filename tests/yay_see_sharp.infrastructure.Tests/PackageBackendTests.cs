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

namespace yay_see_sharp.infrastructure.Tests;

public class PackageBackendTests
{
    private static void SetupPacman(Mock<ICommandRunner> runner, string[] arguments, int exitCode, params string[] lines)
    {
        var output = lines.Select(line => new CommandOutput(CommandOutputKind.StandardOutput, line, DateTimeOffset.UtcNow)).ToArray();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "pacman" && request.Arguments.SequenceEqual(arguments)),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(exitCode, output, false));
    }

    /// <summary>Wires up a fully healthy set of pacman statistics queries so tests that only care about one field don't also have to stub the other five. Also stubs the mocked parser's AUR-confirmation call (used internally by <c>PacmanQueryService</c>, not just via <see cref="IYayOutputParser"/> setups the test itself makes) so `hello-git` confirms as AUR.</summary>
    private static void SetupHealthyStatisticsQueries(Mock<ICommandRunner> runner, Mock<IYayOutputParser>? parser = null)
    {
        SetupPacman(runner, ["-Qq"], 0, "hello", "firefox");
        SetupPacman(runner, ["-Qe"], 0, "hello");
        SetupPacman(runner, ["-Qd"], 0, "firefox");
        SetupPacman(runner, ["-Qdt"], 0);
        SetupPacman(runner, ["-Qm"], 0, "hello-git 1.0-1");
        SetupPacman(runner, ["-Qu"], 0, "firefox 128.0-1 -> 129.0-1");
        SetupPacman(runner, ["-Qi"], 0, "Name : hello", "Installed Size : 1.00 MiB", "", "Name : firefox", "Installed Size : 2.00 MiB");
        SetupAurConfirmation(runner, "hello-git");
        parser?.Setup(item => item.ParseAurConfirmedNames(It.IsAny<string>()))
            .Returns(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello-git" });
    }

    /// <summary>Stubs the bulk `yay -Si -- &lt;names&gt;` AUR-confirmation query so a foreign (`pacman -Qm`) name is confirmed as AUR rather than merely Foreign.</summary>
    private static void SetupAurConfirmation(Mock<ICommandRunner> runner, params string[] confirmedNames)
    {
        var arguments = new List<string> { "-Si", "--" };
        arguments.AddRange(confirmedNames);
        var lines = confirmedNames.SelectMany(name => new[] { $"Repository : aur", $"Name : {name}", string.Empty }).ToArray();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "yay" && request.Arguments.SequenceEqual(arguments)),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, lines.Select(line => new CommandOutput(CommandOutputKind.StandardOutput, line, DateTimeOffset.UtcNow)).ToArray(), false));
    }

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
                    request.Arguments.SequenceEqual(new[] { "-Ss", "--", "hello" })),
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
    public async Task Yay_search_adds_a_double_dash_separator_so_a_query_starting_with_a_dash_cannot_be_read_as_an_option()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "yay" &&
                    request.Arguments.SequenceEqual(new[] { "-Ss", "--", "--upgrade" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));
        parser.Setup(item => item.ParseSearch(It.IsAny<string>())).Returns([]);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        await backend.SearchAsync("--upgrade");

        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Ss", "--", "--upgrade" })),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        parser.Setup(item => item.ParseInfo("info output", null)).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("hello");

        await Assert.That(details).IsEqualTo(expected);
    }

    [Test]
    public async Task Yay_get_details_falls_back_to_official_sync_info_when_not_installed()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var expected = new PackageDetails(
            new PackageSummary("hello", "2.12.1-1", "Greeting utility", PackageSource.Official, 0, PackageState.NotInstalled),
            null,
            null,
            [],
            []);
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Qi", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Si", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [new CommandOutput(CommandOutputKind.StandardOutput, "sync info", DateTimeOffset.UtcNow)], false));
        parser.Setup(item => item.ParseInfo("sync info", PackageSource.Official)).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("hello");

        await Assert.That(details).IsEqualTo(expected);
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Sia", "hello" })),
            It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_get_details_falls_back_to_aur_sync_info_when_not_installed_and_not_in_official_repos()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var expected = new PackageDetails(
            new PackageSummary("hello-git", "2.12.1.r4", "AUR package", PackageSource.Aur, 0, PackageState.NotInstalled),
            null,
            null,
            [],
            []);
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Qi", "hello-git" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Si", "hello-git" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "-Sia", "hello-git" })),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [new CommandOutput(CommandOutputKind.StandardOutput, "aur info", DateTimeOffset.UtcNow)], false));
        parser.Setup(item => item.ParseInfo("aur info", PackageSource.Aur)).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("hello-git");

        await Assert.That(details).IsEqualTo(expected);
    }

    [Test]
    public async Task Yay_get_details_returns_null_when_all_three_queries_fail()
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
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Test]
    public async Task Yay_get_details_returns_null_for_an_invalid_package_name_without_running_any_command()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var details = await backend.GetDetailsAsync("--not-a-package");

        await Assert.That(details).IsNull();
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_get_updates_delegates_to_parser_with_foreign_package_names()
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
        SetupPacman(runner, ["-Qm"], 0, "hello-git 1.0-1");
        SetupAurConfirmation(runner, "hello-git");
        parser.Setup(item => item.ParseUpdates("updates output", It.IsAny<IReadOnlySet<string>>(), It.IsAny<IReadOnlySet<string>>())).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var updates = await backend.GetUpdatesAsync();

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Name).IsEqualTo("hello");
    }

    [Test]
    public async Task Yay_get_updates_records_last_update_check_on_success()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "yay" && request.Arguments.SequenceEqual(new[] { "-Qu" })),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));
        SetupPacman(runner, ["-Qm"], 0);
        SetupHealthyStatisticsQueries(runner, parser);
        parser.Setup(item => item.ParseUpdates(It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<IReadOnlySet<string>>())).Returns([]);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var before = DateTimeOffset.UtcNow;
        await backend.GetUpdatesAsync();
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.LastUpdateCheck).IsNotNull();
        await Assert.That(statistics.LastUpdateCheck!.Value).IsGreaterThanOrEqualTo(before);
    }

    [Test]
    public async Task Yay_get_updates_treats_exit_code_1_as_no_updates_not_a_failure()
    {
        // yay -Qu (like pacman -Qu) exits 1 with empty output when there are simply no updates —
        // that must read as an empty list, not throw and surface as a dashboard error.
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "yay" && request.Arguments.SequenceEqual(new[] { "-Qu" })),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var updates = await backend.GetUpdatesAsync();

        await Assert.That(updates.Count).IsEqualTo(0);
        parser.Verify(item => item.ParseUpdates(
            It.IsAny<string>(), It.IsAny<IReadOnlySet<string>>(), It.IsAny<IReadOnlySet<string>>()), Times.Never);
    }

    [Test]
    public async Task Yay_get_updates_with_exit_code_1_still_records_last_update_check()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "yay" && request.Arguments.SequenceEqual(new[] { "-Qu" })),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));
        SetupHealthyStatisticsQueries(runner, parser);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var before = DateTimeOffset.UtcNow;
        await backend.GetUpdatesAsync();
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.LastUpdateCheck).IsNotNull();
        await Assert.That(statistics.LastUpdateCheck!.Value).IsGreaterThanOrEqualTo(before);
    }

    [Test]
    public async Task Yay_get_installed_packages_delegates_to_parser_with_foreign_package_names()
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
        SetupPacman(runner, ["-Qm"], 0);
        parser.Setup(item => item.ParseInstalled("installed output", It.IsAny<IReadOnlySet<string>>(), It.IsAny<IReadOnlySet<string>>())).Returns(expected);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var installed = await backend.GetInstalledPackagesAsync();

        await Assert.That(installed.Count).IsEqualTo(1);
        await Assert.That(installed[0].Name).IsEqualTo("hello");
    }

    [Test]
    public async Task Yay_installed_parser_reads_name_and_version_pairs()
    {
        var parser = new YayOutputParser();
        const string output = "hello 2.12.1-1\nfirefox 128.0-1\n";

        var results = parser.ParseInstalled(output);

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Name).IsEqualTo("hello");
        await Assert.That(results[0].Version).IsEqualTo("2.12.1-1");
        await Assert.That(results[0].State).IsEqualTo(PackageState.Installed);
        await Assert.That(results[1].Name).IsEqualTo("firefox");
    }

    [Test]
    public async Task Parser_classifies_an_unconfirmed_foreign_installed_package_as_foreign_and_others_as_official()
    {
        var parser = new YayOutputParser();
        const string output = "hello 2.12.1-1\nhello-git 2.12.1.r4-1\n";
        var foreign = new HashSet<string> { "hello-git" };

        var results = parser.ParseInstalled(output, foreign);

        await Assert.That(results.Single(p => p.Name == "hello").Source).IsEqualTo(PackageSource.Official);
        await Assert.That(results.Single(p => p.Name == "hello-git").Source).IsEqualTo(PackageSource.Foreign);
    }

    [Test]
    public async Task Parser_classifies_a_confirmed_aur_installed_package_as_aur()
    {
        var parser = new YayOutputParser();
        const string output = "hello 2.12.1-1\nhello-git 2.12.1.r4-1\n";
        var foreign = new HashSet<string> { "hello-git" };
        var confirmedAur = new HashSet<string> { "hello-git" };

        var results = parser.ParseInstalled(output, foreign, confirmedAur);

        await Assert.That(results.Single(p => p.Name == "hello").Source).IsEqualTo(PackageSource.Official);
        await Assert.That(results.Single(p => p.Name == "hello-git").Source).IsEqualTo(PackageSource.Aur);
    }

    [Test]
    public async Task Parser_classifies_an_unconfirmed_foreign_update_as_foreign_and_others_as_official()
    {
        var parser = new YayOutputParser();
        const string output = "hello 2.12.1-1 -> 2.12.2-1\nhello-git 2.12.1.r3-1 -> 2.12.1.r4-1\n";
        var foreign = new HashSet<string> { "hello-git" };

        var results = parser.ParseUpdates(output, foreign);

        await Assert.That(results.Single(p => p.Name == "hello").Source).IsEqualTo(PackageSource.Official);
        await Assert.That(results.Single(p => p.Name == "hello-git").Source).IsEqualTo(PackageSource.Foreign);
    }

    [Test]
    public async Task Parser_classifies_a_confirmed_aur_update_as_aur()
    {
        var parser = new YayOutputParser();
        const string output = "hello 2.12.1-1 -> 2.12.2-1\nhello-git 2.12.1.r3-1 -> 2.12.1.r4-1\n";
        var foreign = new HashSet<string> { "hello-git" };
        var confirmedAur = new HashSet<string> { "hello-git" };

        var results = parser.ParseUpdates(output, foreign, confirmedAur);

        await Assert.That(results.Single(p => p.Name == "hello-git").Source).IsEqualTo(PackageSource.Aur);
    }

    [Test]
    public async Task Parser_parses_aur_confirmed_names_from_a_bulk_yay_si_response()
    {
        var parser = new YayOutputParser();
        const string output =
            "Repository : aur\nName : hello-git\nVersion : 2.12.1.r4-1\n\n" +
            "Repository : core\nName : hello\nVersion : 2.12.1-1\n";

        var confirmed = parser.ParseAurConfirmedNames(output);

        await Assert.That(confirmed.Contains("hello-git")).IsTrue();
        await Assert.That(confirmed.Contains("hello")).IsFalse();
    }

    [Test]
    public async Task Parser_defaults_to_official_when_no_foreign_package_set_is_supplied()
    {
        var parser = new YayOutputParser();
        const string output = "hello-git 2.12.1.r4-1\n";

        var results = parser.ParseInstalled(output);

        await Assert.That(results[0].Source).IsEqualTo(PackageSource.Official);
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
    public async Task Yay_get_statistics_reports_installed_count_from_pacman()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        SetupHealthyStatisticsQueries(runner, parser);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.InstalledCount).IsEqualTo(2);
    }

    [Test]
    public async Task Yay_get_statistics_reports_explicit_dependency_aur_orphan_updates_and_size_fields()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        SetupHealthyStatisticsQueries(runner, parser);

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.ExplicitCount).IsEqualTo(1);
        await Assert.That(statistics.DependencyCount).IsEqualTo(1);
        await Assert.That(statistics.AurCount).IsEqualTo(1);
        await Assert.That(statistics.OrphanCount).IsEqualTo(0);
        await Assert.That(statistics.UpdatesAvailable).IsEqualTo(1);
        await Assert.That(statistics.InstalledSizeBytes).IsEqualTo((long)((1.00 + 2.00) * 1024 * 1024));
    }

    [Test]
    public async Task Yay_get_statistics_reports_zero_not_unknown_when_a_filter_query_has_no_matches()
    {
        // pacman -Qdt (and the other -Q filters) legitimately exit 1 with empty output when there
        // are simply zero matches — that must read as a real 0, not "unknown".
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        SetupHealthyStatisticsQueries(runner, parser);
        SetupPacman(runner, ["-Qdt"], 1); // no output at all -> zero orphans

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var statistics = await backend.GetStatisticsAsync();

        await Assert.That(statistics.OrphanCount).IsEqualTo(0);
    }

    [Test]
    public async Task Yay_get_statistics_reports_a_field_as_unknown_when_its_query_genuinely_fails()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        SetupHealthyStatisticsQueries(runner, parser);
        // A real failure: non-zero exit *with* error output, not the "no matches" shape.
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "pacman" && request.Arguments.SequenceEqual(new[] { "-Qd" })),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(2, [new CommandOutput(CommandOutputKind.StandardError, "pacman: unexpected error", DateTimeOffset.UtcNow)], false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var statistics = await backend.GetStatisticsAsync();

        // The failure of one query must not corrupt the others, and must not masquerade as zero.
        await Assert.That(statistics.DependencyCount).IsNull();
        await Assert.That(statistics.InstalledCount).IsEqualTo(2);
        await Assert.That(statistics.ExplicitCount).IsEqualTo(1);
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
    public async Task Yay_install_rejects_a_package_name_starting_with_a_dash_without_running_any_command()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("--noconfirm"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_install_rejects_a_package_name_with_an_invalid_character_without_running_any_command()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello; rm -rf ~"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_install_accepts_package_names_using_the_full_arch_naming_character_set()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        const string name = "lib32-openssl@1.0+patch.el7";
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.Contains(name)),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync(name))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
    }

    [Test]
    public async Task Yay_install_adds_builddir_when_a_build_directory_policy_resolves_to_a_writable_path()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var tempDir = Directory.CreateTempSubdirectory();
        try
        {
            var policy = new Mock<IBuildDirectoryPolicy>();
            policy.Setup(p => p.BuildDirectory).Returns(tempDir.FullName);

            runner.Setup(item => item.RunAsync(
                    It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(
                        new[] { "--needed", "--noconfirm", "--builddir", tempDir.FullName, "-S", "hello" })),
                    It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult(0, [], false));

            var backend = new YayPackageBackend(runner.Object, parser.Object, buildDirectoryPolicy: policy.Object);
            var progress = new List<PackageOperationProgress>();

            await foreach (var item in backend.InstallAsync("hello"))
            {
                progress.Add(item);
            }

            await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Test]
    public async Task Yay_install_expands_a_tilde_prefixed_build_directory_to_the_user_home()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var subdirName = ".yay-see-sharp-test-" + Guid.NewGuid().ToString("N");
        var expanded = Path.Combine(home, subdirName);
        Directory.CreateDirectory(expanded);
        try
        {
            var policy = new Mock<IBuildDirectoryPolicy>();
            policy.Setup(p => p.BuildDirectory).Returns("~/" + subdirName);

            runner.Setup(item => item.RunAsync(
                    It.Is<CommandRequest>(request => request.Arguments.Contains(expanded)),
                    It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CommandResult(0, [], false));

            var backend = new YayPackageBackend(runner.Object, parser.Object, buildDirectoryPolicy: policy.Object);
            var progress = new List<PackageOperationProgress>();

            await foreach (var item in backend.InstallAsync("hello"))
            {
                progress.Add(item);
            }

            await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        }
        finally
        {
            Directory.Delete(expanded, recursive: true);
        }
    }

    [Test]
    public async Task Yay_install_fails_gracefully_when_the_configured_build_directory_does_not_exist()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var policy = new Mock<IBuildDirectoryPolicy>();
        policy.Setup(p => p.BuildDirectory).Returns("/does/not/exist/" + Guid.NewGuid());

        var backend = new YayPackageBackend(runner.Object, parser.Object, buildDirectoryPolicy: policy.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_install_omits_builddir_entirely_when_no_build_directory_policy_is_configured()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.Arguments.SequenceEqual(new[] { "--needed", "--noconfirm", "-S", "hello" })),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));

        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.InstallAsync("hello"))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
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
    public async Task Yay_uninstall_rejects_an_invalid_package_name_without_running_any_command()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UninstallAsync("-Rns", removeOrphans: true))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task Yay_update_rejects_an_invalid_package_name_among_the_selected_packages()
    {
        var runner = new Mock<ICommandRunner>();
        var parser = new Mock<IYayOutputParser>();
        var backend = new YayPackageBackend(runner.Object, parser.Object);
        var progress = new List<PackageOperationProgress>();

        await foreach (var item in backend.UpdateAsync(["hello", "--noconfirm"]))
        {
            progress.Add(item);
        }

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Yay_search_parser_reads_repository_name_version_and_description()
    {
        var parser = new YayOutputParser();
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
        var parser = new YayOutputParser();
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
    public async Task Yay_info_parser_falls_back_to_the_source_hint_when_the_repository_field_is_absent()
    {
        var parser = new YayOutputParser();
        const string output = "Name            : hello-git\n" +
            "Version         : 2.12.1.r4-1\n" +
            "Description     : AUR development package.\n";

        var details = parser.ParseInfo(output, PackageSource.Aur);

        await Assert.That(details).IsNotNull();
        await Assert.That(details!.Summary.Source).IsEqualTo(PackageSource.Aur);
        await Assert.That(details.Summary.State).IsEqualTo(PackageState.NotInstalled);
    }

    [Test]
    public async Task Yay_updates_parser_reads_current_and_available_versions()
    {
        var parser = new YayOutputParser();
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
            var info = detector.CreateBackendInfo(snapshot, yayAvailable: true);

            // Non-Arch stays Demo even when yay happens to be on PATH — Real mode requires both.
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
            var info = detector.CreateBackendInfo(detector.Detect(), yayAvailable: true);

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
    public async Task Distribution_detector_selects_real_yay_mode_for_cachyos()
    {
        var osRelease = Path.GetTempFileName();
        await File.WriteAllTextAsync(osRelease, "ID=cachyos\nPRETTY_NAME=CachyOS\n");
        try
        {
            var detector = new LinuxDistributionDetector(osRelease);
            var info = detector.CreateBackendInfo(detector.Detect(), yayAvailable: true);

            await Assert.That(info.Mode).IsEqualTo(BackendMode.Real);
            await Assert.That(info.IsSupported).IsTrue();
        }
        finally
        {
            File.Delete(osRelease);
        }
    }

    [Test]
    public async Task Distribution_detector_reports_unavailable_for_arch_without_yay_on_path()
    {
        var osRelease = Path.GetTempFileName();
        await File.WriteAllTextAsync(osRelease, "ID=arch\nPRETTY_NAME=Arch Linux\n");
        try
        {
            var detector = new LinuxDistributionDetector(osRelease);
            var info = detector.CreateBackendInfo(detector.Detect(), yayAvailable: false);

            await Assert.That(info.Mode).IsEqualTo(BackendMode.Unavailable);
            await Assert.That(info.IsSupported).IsFalse();
            await Assert.That(info.Warning).IsNotNull();
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
