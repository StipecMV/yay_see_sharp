using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Privilege;

/// <summary>Shared "ensure sudo elevation before a privileged operation" helper, used by every backend/installer that runs a privileged command — keeps the Granted/Cancelled/Failed → PackageOperationProgress mapping in one place.</summary>
public static class PrivilegeElevationExtensions
{
    /// <summary>Returns null when elevation was granted (or no IPrivilegeService is configured, e.g. in tests) so the caller should proceed; otherwise returns the terminal progress to yield instead of running the command — cancelling the prompt cancels the operation, it never throws.</summary>
    public static async Task<PackageOperationProgress?> TryElevateAsync(
        this IPrivilegeService? privilegeService,
        PackageOperationKind kind,
        string? displayCommand,
        CancellationToken cancellationToken)
    {
        if (privilegeService is null)
        {
            return null;
        }

        var result = await privilegeService.RequestElevationAsync(cancellationToken);
        return result switch
        {
            PrivilegeResult.Granted => null,
            PrivilegeResult.Cancelled => new PackageOperationProgress(
                kind, PackageOperationStage.Cancelled, 0, "Authentication cancelled.", displayCommand),
            PrivilegeResult.Failed => new PackageOperationProgress(
                kind, PackageOperationStage.Failed, 0, "Authentication failed.", displayCommand),
            _ => null,
        };
    }
}
