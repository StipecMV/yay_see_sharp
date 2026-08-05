namespace yay_see_sharp.domain.Models;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum PackageSource
{
    Official,
    Aur,
}

public enum PackageState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
}

public enum BackendMode
{
    Real,
    Demo,
}

public sealed record PackageSummary(
    string Name,
    string Version,
    string Description,
    PackageSource Source,
    long InstalledSizeBytes,
    PackageState State,
    string? IconUrl = null,
    int? Votes = null);

public sealed record PackageDependency(
    string Name,
    string Version,
    bool IsOrphan);

public sealed record PackageDetails(
    PackageSummary Summary,
    string? Maintainer,
    string? Homepage,
    IReadOnlyList<PackageDependency> Dependencies,
    IReadOnlyList<string> Files);

public sealed record PackageStatistics(
    int InstalledCount,
    int ExplicitCount,
    int DependencyCount,
    int AurCount,
    int UpdatesAvailable,
    long InstalledSizeBytes,
    int OrphanCount,
    DateTimeOffset? LastUpdateCheck);

public sealed record UpdateInfo(
    string Name,
    string CurrentVersion,
    string AvailableVersion,
    PackageSource Source,
    long DownloadSizeBytes);

public enum PackageOperationKind
{
    Install,
    Uninstall,
    Update,
}

public enum PackageOperationStage
{
    Preparing,
    ResolvingDependencies,
    Downloading,
    Applying,
    Verifying,
    Completed,
    Failed,
    Cancelled,
}

public sealed record PackageOperationProgress(
    PackageOperationKind Kind,
    PackageOperationStage Stage,
    int Percent,
    string Message,
    string? Command = null,
    string? Output = null);

public sealed record BackendInfo(
    string DistributionId,
    string DistributionName,
    string PackageManager,
    BackendMode Mode,
    bool IsSupported,
    string? Warning = null);
