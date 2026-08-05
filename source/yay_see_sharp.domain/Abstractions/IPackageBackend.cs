using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

public interface IPackageBackend
{
    BackendInfo Info { get; }

    Task<IReadOnlyList<PackageSummary>> SearchAsync(
        string query,
        PackageSource? source = null,
        CancellationToken cancellationToken = default);

    Task<PackageDetails?> GetDetailsAsync(
        string packageName,
        CancellationToken cancellationToken = default);

    Task<PackageStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UpdateInfo>> GetUpdatesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PackageSummary>> GetInstalledPackagesAsync(
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PackageOperationProgress> InstallAsync(
        string packageName,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PackageOperationProgress> UninstallAsync(
        string packageName,
        bool removeOrphans,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<PackageOperationProgress> UpdateAsync(
        IReadOnlyCollection<string> packageNames,
        CancellationToken cancellationToken = default);
}
