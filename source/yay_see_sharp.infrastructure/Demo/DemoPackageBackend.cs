using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Demo;

public sealed class DemoPackageBackend : IPackageBackend
{
    private sealed class DemoPackage
    {
        public required string Name { get; init; }
        public required string Version { get; set; }
        public required string AvailableVersion { get; set; }
        public required string Description { get; init; }
        public required PackageSource Source { get; init; }
        public required long SizeBytes { get; init; }
        public required bool Installed { get; set; }
        public required bool Explicit { get; set; }
        public required string? IconUrl { get; init; }
        public int? Votes { get; init; }
        public required string[] Dependencies { get; init; }
        public required string[] Files { get; init; }
    }

    private readonly Dictionary<string, DemoPackage> _packages;
    private readonly IReadOnlySet<string> _simulatedFailures;
    private DateTimeOffset? _lastUpdateCheck;

    public DemoPackageBackend(BackendInfo? info = null, IReadOnlySet<string>? simulatedFailures = null)
    {
        Info = info ?? new BackendInfo(
            "ubuntu",
            "Ubuntu Demo",
            "demo",
            BackendMode.Demo,
            false,
            "Demo data does not modify the host system.");
        _packages = CreateCatalog().ToDictionary(package => package.Name, StringComparer.OrdinalIgnoreCase);
        _simulatedFailures = simulatedFailures ?? new HashSet<string>();
    }

    public BackendInfo Info { get; }

    public Task<IReadOnlyList<PackageSummary>> SearchAsync(
        string query,
        PackageSource? source = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = query.Trim();
        var results = _packages.Values
            .Where(package => source is null || package.Source == source)
            .Where(package => normalized.Length == 0 ||
                package.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                package.Description.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.Name)
            .Select(ToSummary)
            .ToArray();
        return Task.FromResult<IReadOnlyList<PackageSummary>>(results);
    }

    public Task<PackageDetails?> GetDetailsAsync(
        string packageName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = Find(packageName);
        if (package is null)
        {
            return Task.FromResult<PackageDetails?>(null);
        }

        var dependencies = package.Dependencies
            .Select(name => Find(name))
            .Where(dependency => dependency is not null)
            .Select(dependency => new PackageDependency(
                dependency!.Name,
                dependency.Version,
                dependency.Installed && !dependency.Explicit && IsOrphan(dependency)))
            .ToArray();
        return Task.FromResult<PackageDetails?>(new PackageDetails(
            ToSummary(package),
            package.Source == PackageSource.Aur ? "community-maintained" : "Arch Linux project",
            $"https://packages.example.invalid/{package.Name}",
            dependencies,
            package.Files));
    }

    public Task<PackageStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = _packages.Values.Where(package => package.Installed).ToArray();
        var updates = installed.Count(package => package.Version != package.AvailableVersion);
        var orphans = installed.Count(IsOrphan);
        return Task.FromResult(new PackageStatistics(
            installed.Length,
            installed.Count(package => package.Explicit),
            installed.Count(package => !package.Explicit),
            installed.Count(package => package.Source == PackageSource.Aur),
            updates,
            installed.Sum(package => package.SizeBytes),
            orphans,
            _lastUpdateCheck));
    }

    public Task<IReadOnlyList<UpdateInfo>> GetUpdatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lastUpdateCheck = DateTimeOffset.UtcNow;
        var updates = _packages.Values
            .Where(package => package.Installed && package.Version != package.AvailableVersion)
            .Select(package => new UpdateInfo(
                package.Name,
                package.Version,
                package.AvailableVersion,
                package.Source,
                Math.Max(1, package.SizeBytes / 10)))
            .OrderBy(update => update.Name)
            .ToArray();
        return Task.FromResult<IReadOnlyList<UpdateInfo>>(updates);
    }

    public Task<IReadOnlyList<PackageSummary>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var installed = _packages.Values
            .Where(package => package.Installed)
            .OrderBy(package => package.Name)
            .Select(ToSummary)
            .ToArray();
        return Task.FromResult<IReadOnlyList<PackageSummary>>(installed);
    }

    public async IAsyncEnumerable<PackageOperationProgress> InstallAsync(
        string packageName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var package = Find(packageName);
        if (package is null)
        {
            yield return Progress(PackageOperationKind.Install, PackageOperationStage.Failed, 0, $"Package '{packageName}' was not found.");
            yield break;
        }

        yield return Progress(PackageOperationKind.Install, PackageOperationStage.Preparing, 10, $"Preparing {package.Name}");

        if (!await TryDelayAsync(20, cancellationToken))
        {
            yield return Progress(PackageOperationKind.Install, PackageOperationStage.Cancelled, 0, "Installation cancelled.");
            yield break;
        }

        yield return Progress(PackageOperationKind.Install, PackageOperationStage.ResolvingDependencies, 35, "Resolving dependencies");

        if (_simulatedFailures.Contains(packageName))
        {
            yield return Progress(PackageOperationKind.Install, PackageOperationStage.Failed, 0, $"Installation of {package.Name} failed.");
            yield break;
        }

        foreach (var dependencyName in package.Dependencies)
        {
            if (Find(dependencyName) is { } dependency)
            {
                dependency.Installed = true;
                dependency.Explicit = false;
            }
        }
        yield return Progress(PackageOperationKind.Install, PackageOperationStage.Downloading, 60, "Downloading package data");

        if (!await TryDelayAsync(20, cancellationToken))
        {
            yield return Progress(PackageOperationKind.Install, PackageOperationStage.Cancelled, 0, "Installation cancelled.");
            yield break;
        }

        package.Installed = true;
        package.Explicit = true;
        package.Version = package.AvailableVersion;
        yield return Progress(PackageOperationKind.Install, PackageOperationStage.Verifying, 90, "Verifying installation");
        yield return Progress(PackageOperationKind.Install, PackageOperationStage.Completed, 100, $"Installed {package.Name}");
    }

    public async IAsyncEnumerable<PackageOperationProgress> UninstallAsync(
        string packageName,
        bool removeOrphans,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var package = Find(packageName);
        if (package is null)
        {
            yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Failed, 0, $"Package '{packageName}' was not found.");
            yield break;
        }

        yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Preparing, 15, $"Preparing removal of {package.Name}");

        if (!await TryDelayAsync(20, cancellationToken))
        {
            yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Cancelled, 0, "Removal cancelled.");
            yield break;
        }

        if (_simulatedFailures.Contains(packageName))
        {
            yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Failed, 0, $"Removal of {package.Name} failed.");
            yield break;
        }

        package.Installed = false;
        package.Explicit = false;
        yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Applying, 65, $"Removing {package.Name}");
        if (removeOrphans)
        {
            foreach (var orphan in _packages.Values.Where(IsOrphan).ToArray())
            {
                orphan.Installed = false;
                orphan.Explicit = false;
            }
        }
        yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Verifying, 90, "Verifying package files are removed");
        yield return Progress(PackageOperationKind.Uninstall, PackageOperationStage.Completed, 100, $"Removed {package.Name}");
    }

    public async IAsyncEnumerable<PackageOperationProgress> UpdateAsync(
        IReadOnlyCollection<string> packageNames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Progress(PackageOperationKind.Update, PackageOperationStage.Preparing, 10, "Preparing updates");

        if (!await TryDelayAsync(20, cancellationToken))
        {
            yield return Progress(PackageOperationKind.Update, PackageOperationStage.Cancelled, 0, "Update cancelled.");
            yield break;
        }

        DemoPackage[] selected;
        if (packageNames.Count == 0)
        {
            selected = _packages.Values.Where(package => package.Installed && package.Version != package.AvailableVersion).ToArray();
        }
        else
        {
            // All-or-nothing, matching yay/pacman: an update naming an unknown package fails the
            // whole invocation rather than silently skipping it.
            var missing = packageNames.Where(name => Find(name) is null).ToArray();
            if (missing.Length > 0)
            {
                yield return Progress(
                    PackageOperationKind.Update, PackageOperationStage.Failed, 0, $"Package '{missing[0]}' was not found.");
                yield break;
            }

            selected = packageNames.Select(Find).Cast<DemoPackage>().ToArray();
        }

        if (selected.Any(package => _simulatedFailures.Contains(package.Name)))
        {
            yield return Progress(PackageOperationKind.Update, PackageOperationStage.Failed, 0, "Update failed.");
            yield break;
        }

        foreach (var package in selected)
        {
            package.Version = package.AvailableVersion;
        }
        yield return Progress(PackageOperationKind.Update, PackageOperationStage.Verifying, 90, "Verifying updates");
        yield return Progress(PackageOperationKind.Update, PackageOperationStage.Completed, 100, $"Updated {selected.Length} package(s)");
    }

    private DemoPackage? Find(string name) => _packages.GetValueOrDefault(name);

    private bool IsOrphan(DemoPackage package) => package.Installed && !package.Explicit &&
        !_packages.Values.Any(candidate => candidate.Installed && candidate.Dependencies.Contains(package.Name, StringComparer.OrdinalIgnoreCase));

    private PackageSummary ToSummary(DemoPackage package) => new(
        package.Name,
        package.Version,
        package.Description,
        package.Source,
        package.SizeBytes,
        !package.Installed ? PackageState.NotInstalled :
            package.Version != package.AvailableVersion ? PackageState.UpdateAvailable : PackageState.Installed,
        package.IconUrl,
        package.Votes);

    private static PackageOperationProgress Progress(
        PackageOperationKind kind,
        PackageOperationStage stage,
        int percent,
        string message) => new(kind, stage, percent, message, $"demo {kind.ToString().ToLowerInvariant()}", message);

    /// <summary>Awaits a delay and reports whether it ran to completion, so callers can yield a graceful Cancelled progress event instead of letting OperationCanceledException escape the async iterator (yield return is not allowed inside a catch block).</summary>
    private static async Task<bool> TryDelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(milliseconds, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static IEnumerable<DemoPackage> CreateCatalog()
    {
        yield return new DemoPackage { Name = "firefox", Version = "128.0", AvailableVersion = "129.0", Description = "Fast, private and open web browser.", Source = PackageSource.Official, SizeBytes = 280_000_000, Installed = true, Explicit = true, IconUrl = "https://cdn.simpleicons.org/firefox/FF7139", Dependencies = ["gtk3", "libx11"], Files = ["/usr/bin/firefox", "/usr/share/applications/firefox.desktop"] };
        yield return new DemoPackage { Name = "code", Version = "1.92.0", AvailableVersion = "1.92.0", Description = "Visual Studio Code editor packaged for Arch Linux.", Source = PackageSource.Aur, SizeBytes = 410_000_000, Installed = true, Explicit = true, IconUrl = "https://cdn.simpleicons.org/visualstudiocode/007ACC", Votes = 1840, Dependencies = ["gtk3"], Files = ["/usr/bin/code", "/usr/share/applications/code.desktop"] };
        yield return new DemoPackage { Name = "git", Version = "2.46.0", AvailableVersion = "2.46.0", Description = "Distributed version control system.", Source = PackageSource.Official, SizeBytes = 31_000_000, Installed = true, Explicit = true, IconUrl = "https://cdn.simpleicons.org/git/F05032", Dependencies = ["perl"], Files = ["/usr/bin/git", "/usr/share/man/man1/git.1.gz"] };
        yield return new DemoPackage { Name = "gtk3", Version = "3.24.42", AvailableVersion = "3.24.42", Description = "GObject-based multi-platform GUI toolkit.", Source = PackageSource.Official, SizeBytes = 18_000_000, Installed = true, Explicit = false, IconUrl = null, Dependencies = [], Files = ["/usr/lib/libgtk-3.so"] };
        yield return new DemoPackage { Name = "libx11", Version = "1.8.10", AvailableVersion = "1.8.10", Description = "X11 client-side library.", Source = PackageSource.Official, SizeBytes = 4_000_000, Installed = true, Explicit = false, IconUrl = null, Dependencies = [], Files = ["/usr/lib/libX11.so"] };
        yield return new DemoPackage { Name = "perl", Version = "5.40.0", AvailableVersion = "5.40.0", Description = "Highly capable, feature-rich programming language.", Source = PackageSource.Official, SizeBytes = 48_000_000, Installed = true, Explicit = false, IconUrl = null, Dependencies = [], Files = ["/usr/bin/perl"] };
        yield return new DemoPackage { Name = "hello", Version = "2.12.1", AvailableVersion = "2.12.1", Description = "A friendly demo package used by integration tests.", Source = PackageSource.Official, SizeBytes = 1_000_000, Installed = false, Explicit = false, IconUrl = null, Dependencies = [], Files = ["/usr/bin/hello", "/usr/share/info/hello.info.gz"] };
        yield return new DemoPackage { Name = "spotify", Version = "1.2.42", AvailableVersion = "1.2.43", Description = "Music streaming client from the AUR.", Source = PackageSource.Aur, SizeBytes = 190_000_000, Installed = false, Explicit = false, IconUrl = "https://cdn.simpleicons.org/spotify/1ED760", Votes = 612, Dependencies = ["gtk3"], Files = ["/opt/spotify/spotify", "/usr/share/applications/spotify.desktop"] };
    }
}
