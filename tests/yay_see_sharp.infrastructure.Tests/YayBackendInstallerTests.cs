using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using Moq;

namespace yay_see_sharp.infrastructure.Tests;

public class YayBackendInstallerTests
{
    private static Mock<ICommandRunner> CreateRunner() => new();

    // --- CachyOS: plain `pacman -S` path ---

    [Test]
    public async Task CachyOS_display_command_is_the_plain_pacman_install()
    {
        var installer = new YayBackendInstaller(CreateRunner().Object, isCachyOs: true);

        await Assert.That(installer.DisplayCommand).IsEqualTo("sudo pacman -S --needed --noconfirm yay");
    }

    [Test]
    public async Task CachyOS_success_runs_exactly_sudo_pacman_dash_S_and_reports_completion()
    {
        var runner = CreateRunner();
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request =>
                    request.FileName == "sudo" &&
                    request.Arguments.SequenceEqual(new[] { "pacman", "-S", "--needed", "--noconfirm", "yay" }) &&
                    request.WorkingDirectory == null),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: true);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(progress[^1].Percent).IsEqualTo(100);
        runner.Verify(item => item.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task CachyOS_failure_reports_failed_with_the_exit_code_in_the_message()
    {
        var runner = CreateRunner();
        runner.Setup(item => item.RunAsync(
                It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [new CommandOutput(CommandOutputKind.StandardError, "target not found", DateTimeOffset.UtcNow)], false));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: true);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(progress[^1].Message).Contains("1");
        await Assert.That(progress[^1].Output).IsEqualTo("target not found");
    }

    [Test]
    public async Task CachyOS_cancellation_reports_cancelled_not_failed()
    {
        var runner = CreateRunner();
        runner.Setup(item => item.RunAsync(
                It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], true));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: true);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
    }

    // --- Plain Arch: AUR clone + makepkg bootstrap path ---

    [Test]
    public async Task Plain_arch_display_command_is_the_aur_bootstrap()
    {
        var installer = new YayBackendInstaller(CreateRunner().Object, isCachyOs: false);

        await Assert.That(installer.DisplayCommand).Contains("git clone");
        await Assert.That(installer.DisplayCommand).Contains("makepkg");
    }

    [Test]
    public async Task Plain_arch_success_clones_then_builds_in_the_same_temp_directory_and_reports_completion()
    {
        var runner = CreateRunner();
        string? clonedInto = null;
        string? builtIn = null;

        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "git"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                AssertTrue(request.Arguments.SequenceEqual(
                    new[] { "clone", "--depth", "1", "https://aur.archlinux.org/yay.git", request.Arguments[^1] }));
                clonedInto = request.Arguments[^1];
            })
            .ReturnsAsync(new CommandResult(0, [], false));

        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "makepkg"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                builtIn = request.WorkingDirectory;
            })
            .ReturnsAsync(new CommandResult(0, [], false));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: false);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(clonedInto).IsNotNull();
        await Assert.That(builtIn).IsEqualTo(clonedInto);
        await Assert.That(clonedInto!).StartsWith(Path.Combine(Path.GetTempPath(), "yay-see-sharp-yay-install-"));

        // The temp build directory is removed once the operation finishes.
        await Assert.That(Directory.Exists(clonedInto)).IsFalse();
    }

    [Test]
    public async Task Plain_arch_clone_failure_reports_failed_and_still_cleans_up_the_temp_directory()
    {
        var runner = CreateRunner();
        string? buildRoot = null;
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "git"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                buildRoot = request.Arguments[^1];
            })
            .ReturnsAsync(new CommandResult(128, [new CommandOutput(CommandOutputKind.StandardError, "repository not found", DateTimeOffset.UtcNow)], false));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: false);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        runner.Verify(item => item.RunAsync(
            It.Is<CommandRequest>(request => request.FileName == "makepkg"),
            It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Never);
        await Assert.That(buildRoot).IsNotNull();
        await Assert.That(Directory.Exists(buildRoot)).IsFalse();
    }

    [Test]
    public async Task Plain_arch_makepkg_failure_reports_failed_and_still_cleans_up_the_temp_directory()
    {
        var runner = CreateRunner();
        string? buildRoot = null;
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "git"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                buildRoot = request.Arguments[^1];
            })
            .ReturnsAsync(new CommandResult(0, [], false));
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "makepkg"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [new CommandOutput(CommandOutputKind.StandardError, "build failed", DateTimeOffset.UtcNow)], false));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: false);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(buildRoot).IsNotNull();
        await Assert.That(Directory.Exists(buildRoot)).IsFalse();
    }

    [Test]
    public async Task Plain_arch_cancelled_clone_reports_cancelled_and_cleans_up()
    {
        var runner = CreateRunner();
        string? buildRoot = null;
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "git"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                buildRoot = request.Arguments[^1];
            })
            .ReturnsAsync(new CommandResult(0, [], true));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: false);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        await Assert.That(buildRoot).IsNotNull();
        await Assert.That(Directory.Exists(buildRoot)).IsFalse();
    }

    [Test]
    public async Task Plain_arch_cancelled_makepkg_reports_cancelled_and_cleans_up()
    {
        var runner = CreateRunner();
        string? buildRoot = null;
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "git"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .Callback<CommandRequest, IProgress<CommandOutput>?, CancellationToken>((request, _, _) =>
            {
                buildRoot = request.Arguments[^1];
            })
            .ReturnsAsync(new CommandResult(0, [], false));
        runner.Setup(item => item.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "makepkg"),
                It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], true));

        var installer = new YayBackendInstaller(runner.Object, isCachyOs: false);
        var progress = await CollectAsync(installer);

        await Assert.That(progress[^1].Stage).IsEqualTo(PackageOperationStage.Cancelled);
        await Assert.That(buildRoot).IsNotNull();
        await Assert.That(Directory.Exists(buildRoot)).IsFalse();
    }

    private static async Task<List<PackageOperationProgress>> CollectAsync(YayBackendInstaller installer)
    {
        var progress = new List<PackageOperationProgress>();
        await foreach (var item in installer.InstallAsync())
        {
            progress.Add(item);
        }

        return progress;
    }

    private static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Callback assertion failed.");
        }
    }
}
