using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Yay;

public sealed partial class YayPackageBackend
{
    public async IAsyncEnumerable<PackageOperationProgress> UpdateAsync(
        IReadOnlyCollection<string> packageNames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var arguments = packageNames.Count == 0
            ? new[] { "-Syu", "--noconfirm" }
            : new[] { "-S", "--noconfirm", "--needed" }.Concat(packageNames.Select(name => name.Trim())).ToArray();
        var displayCommand = packageNames.Count == 0
            ? "yay -Syu --noconfirm"
            : "yay -S --noconfirm --needed <packages>";

        yield return new PackageOperationProgress(
            PackageOperationKind.Update,
            PackageOperationStage.Preparing,
            5,
            packageNames.Count == 0
                ? "Preparing to update all packages."
                : $"Preparing to update {packageNames.Count} package(s).",
            displayCommand);

        if (await TryElevateAsync(PackageOperationKind.Update, displayCommand, cancellationToken) is { } elevationOutcome)
        {
            yield return elevationOutcome;
            yield break;
        }

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", arguments),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Update,
                PackageOperationStage.Cancelled,
                0,
                "Update cancelled.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Update,
                PackageOperationStage.Failed,
                0,
                $"Update failed with exit code {result.ExitCode}.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Update,
            PackageOperationStage.Completed,
            100,
            "Update completed.",
            displayCommand,
            result.CombinedText);
    }
}
