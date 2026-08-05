namespace yay_see_sharp.infrastructure.Privilege;

/// <summary>Thin seam over the real `sudo` process calls so SudoPrivilegeService is unit-testable without spawning sudo.</summary>
public interface ISudoInvoker
{
    /// <summary>Non-interactive check ("sudo -n -v"): true if the cached timestamp is still valid, false if a password is needed.</summary>
    Task<bool> ValidateTimestampAsync(CancellationToken cancellationToken);

    /// <summary>Refreshes the timestamp cache by piping the password to "sudo -S -v" over stdin. Never put the password on the command line.</summary>
    Task<bool> RefreshWithPasswordAsync(string password, CancellationToken cancellationToken);
}
