using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

/// <summary>Centralized `pacman` query/parsing layer backing the real statistics and AUR/official classification `YayPackageBackend` reports — kept out of the backend itself so each query and its parsing rule has one tested home instead of being scattered across ad-hoc line counts.</summary>
public interface IPacmanQueryService
{
    Task<PackageStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>Names of foreign (not present in any configured repo — AUR, manually installed, external repo, or local) packages, from `pacman -Qm`. Never throws — a failed or empty query yields an empty set, which just means nothing gets classified as Foreign.</summary>
    Task<IReadOnlySet<string>> GetForeignPackageNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Confirms, via a bulk AUR metadata query, which of <paramref name="foreignPackageNames"/> are actually AUR packages rather than some other out-of-repo source. Never throws — a failed query yields an empty set, so callers fall back to classifying those names as Foreign rather than falsely as AUR.</summary>
    Task<IReadOnlySet<string>> GetConfirmedAurPackageNamesAsync(
        IReadOnlySet<string> foreignPackageNames, CancellationToken cancellationToken = default);
}
