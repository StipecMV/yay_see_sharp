using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

/// <summary>Centralized `pacman` query/parsing layer backing the real statistics and AUR/official classification `YayPackageBackend` reports — kept out of the backend itself so each query and its parsing rule has one tested home instead of being scattered across ad-hoc line counts.</summary>
public interface IPacmanQueryService
{
    Task<PackageStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>Names of foreign (AUR/manually-installed, not present in any configured repo) packages, from `pacman -Qm`. Used to classify installed/update entries as AUR vs Official. Never throws — a failed or empty query yields an empty set, which just means nothing gets classified as AUR.</summary>
    Task<IReadOnlySet<string>> GetForeignPackageNamesAsync(CancellationToken cancellationToken = default);
}
