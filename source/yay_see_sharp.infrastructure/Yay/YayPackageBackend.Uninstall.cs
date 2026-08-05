using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Yay;

public sealed partial class YayPackageBackend
{
    public async IAsyncEnumerable<PackageOperationProgress> UninstallAsync(
        string packageName,
        bool removeOrphans,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                "A package name is required.");
            yield break;
        }

        var removeFlag = removeOrphans ? "-Rns" : "-Rn";
        var displayCommand = $"yay --noconfirm {removeFlag} <package>";

        yield return new PackageOperationProgress(
            PackageOperationKind.Uninstall,
            PackageOperationStage.Preparing,
            5,
            $"Preparing removal of {packageName}.",
            displayCommand);

        if (await TryElevateAsync(PackageOperationKind.Uninstall, displayCommand, cancellationToken) is { } elevationOutcome)
        {
            yield return elevationOutcome;
            yield break;
        }

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["--noconfirm", removeFlag, packageName.Trim()]),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Cancelled,
                0,
                "Removal cancelled.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                $"Removal failed with exit code {result.ExitCode}.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Uninstall,
            PackageOperationStage.Completed,
            100,
            $"Removed {packageName.Trim()}.",
            displayCommand,
            result.CombinedText);
    }
}
