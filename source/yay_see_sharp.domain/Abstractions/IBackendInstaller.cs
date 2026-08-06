using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

/// <summary>
/// Installs the recommended real package-manager backend (currently always `yay`) when
/// <see cref="BackendMode.Unavailable"/> is detected — an Arch/CachyOS host that doesn't have it
/// on PATH yet. Only ever invoked as an explicit, user-confirmed action; never runs automatically.
/// </summary>
public interface IBackendInstaller
{
    /// <summary>The exact command this will run, shown to the user for confirmation before <see cref="InstallAsync"/> is called.</summary>
    string DisplayCommand { get; }

    IAsyncEnumerable<PackageOperationProgress> InstallAsync(CancellationToken cancellationToken = default);
}
