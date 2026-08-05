namespace yay_see_sharp.domain.Abstractions;

public enum PrivilegeResult
{
    Granted,
    Cancelled,
    Failed,
}

/// <summary>
/// Manages the sudo timestamp cache that privileged package operations (install/uninstall/update)
/// rely on. Never exposes or persists the password it collects — see SudoPrivilegeService.
/// </summary>
public interface IPrivilegeService
{
    bool IsElevated { get; }

    /// <summary>Ensures elevation is active, prompting for a password if the cached sudo timestamp is missing or expired.</summary>
    Task<PrivilegeResult> RequestElevationAsync(CancellationToken cancellationToken = default);

    /// <summary>Opportunistically extends an already-granted elevation without prompting; a no-op if nothing was granted yet.</summary>
    Task<PrivilegeResult> RefreshIfNeededAsync(CancellationToken cancellationToken = default);
}
