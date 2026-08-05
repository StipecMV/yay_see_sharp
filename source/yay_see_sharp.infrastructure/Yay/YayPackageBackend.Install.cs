using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Yay;

public sealed partial class YayPackageBackend
{
    public async IAsyncEnumerable<PackageOperationProgress> InstallAsync(
        string packageName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                "A package name is required.");
            yield break;
        }

        const string displayCommand = "yay --needed --noconfirm -S <package>";

        yield return new PackageOperationProgress(
            PackageOperationKind.Install,
            PackageOperationStage.Preparing,
            5,
            $"Preparing installation of {packageName}.",
            displayCommand);

        if (await TryElevateAsync(PackageOperationKind.Install, displayCommand, cancellationToken) is { } elevationOutcome)
        {
            yield return elevationOutcome;
            yield break;
        }

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["--needed", "--noconfirm", "-S", packageName.Trim()]),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Cancelled,
                0,
                "Installation cancelled.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                $"Installation failed with exit code {result.ExitCode}.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Install,
            PackageOperationStage.Completed,
            100,
            $"Installed {packageName.Trim()}.",
            displayCommand,
            result.CombinedText);
    }
}
