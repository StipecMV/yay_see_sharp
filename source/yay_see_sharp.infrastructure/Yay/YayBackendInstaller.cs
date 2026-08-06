using System.Runtime.CompilerServices;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Privilege;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Yay;

/// <summary>
/// Installs `yay` itself when it's missing on an Arch/CachyOS host (<see cref="BackendMode.Unavailable"/>).
/// CachyOS ships `yay` in its own repos, so a plain `pacman -S` suffices there; plain Arch has no
/// such package and needs the standard AUR bootstrap (clone + makepkg). Every step uses
/// ArgumentList — no shell string ever carries the build directory or package name.
/// </summary>
public sealed class YayBackendInstaller : IBackendInstaller
{
    private readonly ICommandRunner _commandRunner;
    private readonly IPrivilegeService? _privilegeService;
    private readonly bool _isCachyOs;

    public YayBackendInstaller(ICommandRunner commandRunner, bool isCachyOs, IPrivilegeService? privilegeService = null)
    {
        _commandRunner = commandRunner;
        _isCachyOs = isCachyOs;
        _privilegeService = privilegeService;
    }

    public string DisplayCommand => _isCachyOs
        ? "sudo pacman -S --needed --noconfirm yay"
        : "git clone https://aur.archlinux.org/yay.git <tmp> && makepkg -si --noconfirm";

    public async IAsyncEnumerable<PackageOperationProgress> InstallAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Progress(PackageOperationStage.Preparing, 5, "Preparing yay installation.");

        if (await _privilegeService.TryElevateAsync(PackageOperationKind.InstallBackend, DisplayCommand, cancellationToken)
            is { } elevationOutcome)
        {
            yield return elevationOutcome;
            yield break;
        }

        await foreach (var progress in _isCachyOs
            ? InstallViaPacmanAsync(cancellationToken)
            : InstallViaAurBootstrapAsync(cancellationToken))
        {
            yield return progress;
        }
    }

    private async IAsyncEnumerable<PackageOperationProgress> InstallViaPacmanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("sudo", ["pacman", "-S", "--needed", "--noconfirm", "yay"]),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            yield return Progress(PackageOperationStage.Cancelled, 0, "yay installation cancelled.", result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return Progress(
                PackageOperationStage.Failed, 0, $"yay installation failed with exit code {result.ExitCode}.", result.CombinedText);
            yield break;
        }

        yield return Progress(PackageOperationStage.Completed, 100, "yay installed.", result.CombinedText);
    }

    private async IAsyncEnumerable<PackageOperationProgress> InstallViaAurBootstrapAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buildRoot = Path.Combine(Path.GetTempPath(), $"yay-see-sharp-yay-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(buildRoot);

        try
        {
            yield return Progress(PackageOperationStage.Downloading, 30, "Cloning yay from the AUR.");

            var clone = await _commandRunner.RunAsync(
                new CommandRequest("git", ["clone", "--depth", "1", "https://aur.archlinux.org/yay.git", buildRoot]),
                cancellationToken: cancellationToken);

            if (clone.WasCancelled)
            {
                yield return Progress(PackageOperationStage.Cancelled, 0, "yay installation cancelled.", clone.CombinedText);
                yield break;
            }

            if (!clone.Succeeded)
            {
                yield return Progress(
                    PackageOperationStage.Failed, 0, $"Cloning yay failed with exit code {clone.ExitCode}.", clone.CombinedText);
                yield break;
            }

            yield return Progress(PackageOperationStage.Applying, 70, "Building and installing yay.");

            // makepkg refuses to run as root (by design) and elevates internally via a plain
            // `sudo pacman -U ...` once the package is built — the timestamp refreshed by
            // TryElevateAsync above lets that succeed without a second interactive prompt.
            var build = await _commandRunner.RunAsync(
                new CommandRequest("makepkg", ["-si", "--noconfirm"], WorkingDirectory: buildRoot),
                cancellationToken: cancellationToken);

            if (build.WasCancelled)
            {
                yield return Progress(PackageOperationStage.Cancelled, 0, "yay installation cancelled.", build.CombinedText);
                yield break;
            }

            if (!build.Succeeded)
            {
                yield return Progress(
                    PackageOperationStage.Failed, 0, $"Building yay failed with exit code {build.ExitCode}.", build.CombinedText);
                yield break;
            }

            yield return Progress(PackageOperationStage.Completed, 100, "yay installed.", build.CombinedText);
        }
        finally
        {
            try
            {
                Directory.Delete(buildRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static PackageOperationProgress Progress(PackageOperationStage stage, int percent, string message, string? output = null) =>
        new(PackageOperationKind.InstallBackend, stage, percent, message, Output: output);
}
