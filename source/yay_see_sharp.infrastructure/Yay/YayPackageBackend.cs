using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Privilege;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public sealed class YayPackageBackend : IPackageBackend
{
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;
    private readonly IPrivilegeService? _privilegeService;
    private readonly IPacmanQueryService _pacmanQueryService;
    private readonly IBuildDirectoryPolicy? _buildDirectoryPolicy;

    private DateTimeOffset? _lastUpdateCheck;

    public YayPackageBackend(
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        BackendInfo? info = null,
        IPrivilegeService? privilegeService = null,
        IPacmanQueryService? pacmanQueryService = null,
        IBuildDirectoryPolicy? buildDirectoryPolicy = null)
    {
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
        _pacmanQueryService = pacmanQueryService ?? new PacmanQueryService(commandRunner, outputParser);
        _buildDirectoryPolicy = buildDirectoryPolicy;
        Info = info ?? new BackendInfo(
            "arch",
            "Arch Linux",
            "yay",
            BackendMode.Real,
            true);
    }

    public BackendInfo Info { get; }

    /// <summary>
    /// Resolves the configured AUR build directory into an absolute, existing, writable path
    /// ready to pass as a single <c>--builddir</c> ArgumentList entry (never a shell string). When
    /// no policy is configured at all (e.g. most existing tests, or a caller that doesn't care),
    /// this is a no-op — <c>--builddir</c> is simply omitted and yay uses its own default. When a
    /// path *is* configured but unusable, the caller must fail the operation with the returned
    /// reason rather than silently falling back, per the setting's whole point: it should either
    /// change behavior or tell the user why it didn't.
    /// </summary>
    private (string? Path, string? Error) ResolveBuildDirectory()
    {
        var configured = _buildDirectoryPolicy?.BuildDirectory;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return (null, null);
        }

        var expanded = configured.StartsWith('~')
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                configured.TrimStart('~').TrimStart('/'))
            : configured;

        if (!Directory.Exists(expanded))
        {
            return (null, $"Build directory '{expanded}' does not exist.");
        }

        try
        {
            var probePath = Path.Combine(expanded, $".yay-see-sharp-write-check-{Guid.NewGuid():N}");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"Build directory '{expanded}' is not writable.");
        }

        return (expanded, null);
    }

    public async Task<IReadOnlyList<PackageSummary>> SearchAsync(
        string query,
        PackageSource? source = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var trimmedQuery = query.Trim();

        // "--" tells yay/pacman's getopt-style parser that everything after it is positional,
        // so a query the user typed starting with '-' (e.g. "-Sy") can never be read as an
        // option. Always included, not just when the query looks suspicious, so the command
        // shape is the same for every search and doesn't need a second code path to test.
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Ss", "--", trimmedQuery]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"yay search failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var packages = _outputParser.ParseSearch(result.CombinedText);
        return source is null
            ? packages
            : packages.Where(package => package.Source == source).ToArray();
    }

    public async Task<PackageDetails?> GetDetailsAsync(
        string packageName,
        CancellationToken cancellationToken = default)
    {
        var trimmed = packageName.Trim();
        if (!PackageArgumentValidator.IsValidPackageName(trimmed))
        {
            return null;
        }

        // Installed packages have full local metadata via -Qi. A package the user found through
        // Search may not be installed yet, in which case -Qi has nothing to report — fall back to
        // sync-db info (-Si for official repos, -Sia for AUR) so Search → details still works
        // before the user ever installs anything.
        var installed = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Qi", trimmed]),
            cancellationToken: cancellationToken);
        if (installed.Succeeded)
        {
            return _outputParser.ParseInfo(installed.CombinedText);
        }

        var official = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Si", trimmed]),
            cancellationToken: cancellationToken);
        if (official.Succeeded)
        {
            return _outputParser.ParseInfo(official.CombinedText, PackageSource.Official);
        }

        var aur = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Sia", trimmed]),
            cancellationToken: cancellationToken);
        return aur.Succeeded ? _outputParser.ParseInfo(aur.CombinedText, PackageSource.Aur) : null;
    }

    public async Task<PackageStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var statistics = await _pacmanQueryService.GetStatisticsAsync(cancellationToken);
        return statistics with { LastUpdateCheck = _lastUpdateCheck };
    }

    public async Task<IReadOnlyList<UpdateInfo>> GetUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Qu"]),
            cancellationToken: cancellationToken);

        // yay -Qu (like pacman -Qu) exits 1 when there are simply no updates — a real, valid
        // "nothing to update", not a failed query. Only a non-zero exit that isn't this specific
        // shape counts as an actual failure. WasCancelled is excluded so a genuinely cancelled
        // check never gets misreported as "no updates" just because the killed process happened
        // to report exit code 1.
        if (result.ExitCode == 1 && !result.WasCancelled)
        {
            _lastUpdateCheck = DateTimeOffset.UtcNow;
            return Array.Empty<UpdateInfo>();
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"yay update check failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var foreignNames = await _pacmanQueryService.GetForeignPackageNamesAsync(cancellationToken);
        var confirmedAurNames = await _pacmanQueryService.GetConfirmedAurPackageNamesAsync(foreignNames, cancellationToken);
        _lastUpdateCheck = DateTimeOffset.UtcNow;
        return _outputParser.ParseUpdates(result.CombinedText, foreignNames, confirmedAurNames);
    }

    public async Task<IReadOnlyList<PackageSummary>> GetInstalledPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Q"]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"yay installed package query failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var foreignNames = await _pacmanQueryService.GetForeignPackageNamesAsync(cancellationToken);
        var confirmedAurNames = await _pacmanQueryService.GetConfirmedAurPackageNamesAsync(foreignNames, cancellationToken);
        return _outputParser.ParseInstalled(result.CombinedText, foreignNames, confirmedAurNames);
    }

    public async IAsyncEnumerable<PackageOperationProgress> InstallAsync(
        string packageName,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                "A package name is required.");
            yield break;
        }

        var trimmedName = packageName.Trim();
        if (!PackageArgumentValidator.IsValidPackageName(trimmedName))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                $"'{packageName}' is not a valid package name.");
            yield break;
        }

        var (buildDirectory, buildDirectoryError) = ResolveBuildDirectory();
        if (buildDirectoryError is not null)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                buildDirectoryError);
            yield break;
        }

        var arguments = new List<string> { "--needed", "--noconfirm" };
        if (buildDirectory is not null)
        {
            arguments.Add("--builddir");
            arguments.Add(buildDirectory);
        }

        arguments.Add("-S");
        arguments.Add(trimmedName);

        var displayCommand = buildDirectory is null
            ? "yay --needed --noconfirm -S <package>"
            : $"yay --needed --noconfirm --builddir {buildDirectory} -S <package>";

        yield return new PackageOperationProgress(
            PackageOperationKind.Install,
            PackageOperationStage.Preparing,
            5,
            $"Preparing installation of {trimmedName}.",
            displayCommand);

        if (await _privilegeService.TryElevateAsync(PackageOperationKind.Install, displayCommand, cancellationToken) is { } elevationOutcome)
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
                PackageOperationKind.Install,
                PackageOperationStage.Cancelled,
                0,
                "Installation cancelled.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                $"Installation failed with exit code {result.ExitCode}.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Install,
            PackageOperationStage.Completed,
            100,
            $"Installed {trimmedName}.",
            displayCommand,
            result.CombinedText);
    }

    public async IAsyncEnumerable<PackageOperationProgress> UninstallAsync(
        string packageName,
        bool removeOrphans,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageName))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                "A package name is required.");
            yield break;
        }

        if (!PackageArgumentValidator.IsValidPackageName(packageName.Trim()))
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                $"'{packageName}' is not a valid package name.");
            yield break;
        }

        var removeFlag = removeOrphans ? "-Rns" : "-Rn";
        var displayCommand = $"yay --noconfirm {removeFlag} <package>";

        yield return new PackageOperationProgress(
            PackageOperationKind.Uninstall,
            PackageOperationStage.Preparing,
            5,
            $"Preparing removal of {packageName}.",
            displayCommand);

        if (await _privilegeService.TryElevateAsync(PackageOperationKind.Uninstall, displayCommand, cancellationToken) is { } elevationOutcome)
        {
            yield return elevationOutcome;
            yield break;
        }

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["--noconfirm", removeFlag, packageName.Trim()]),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Cancelled,
                0,
                "Removal cancelled.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        if (!result.Succeeded)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                $"Removal failed with exit code {result.ExitCode}.",
                displayCommand,
                result.CombinedText);
            yield break;
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Uninstall,
            PackageOperationStage.Completed,
            100,
            $"Removed {packageName.Trim()}.",
            displayCommand,
            result.CombinedText);
    }

    public async IAsyncEnumerable<PackageOperationProgress> UpdateAsync(
        IReadOnlyCollection<string> packageNames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var trimmedNames = packageNames.Select(name => name.Trim()).ToArray();
        var invalidName = trimmedNames.FirstOrDefault(name => !PackageArgumentValidator.IsValidPackageName(name));
        if (invalidName is not null)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Update,
                PackageOperationStage.Failed,
                0,
                $"'{invalidName}' is not a valid package name.");
            yield break;
        }

        var (buildDirectory, buildDirectoryError) = ResolveBuildDirectory();
        if (buildDirectoryError is not null)
        {
            yield return new PackageOperationProgress(
                PackageOperationKind.Update,
                PackageOperationStage.Failed,
                0,
                buildDirectoryError);
            yield break;
        }

        var arguments = new List<string>();
        string displayCommand;
        if (trimmedNames.Length == 0)
        {
            arguments.Add("-Syu");
            arguments.Add("--noconfirm");
            if (buildDirectory is not null)
            {
                arguments.Add("--builddir");
                arguments.Add(buildDirectory);
            }

            displayCommand = buildDirectory is null
                ? "yay -Syu --noconfirm"
                : $"yay -Syu --noconfirm --builddir {buildDirectory}";
        }
        else
        {
            arguments.Add("-S");
            arguments.Add("--noconfirm");
            arguments.Add("--needed");
            if (buildDirectory is not null)
            {
                arguments.Add("--builddir");
                arguments.Add(buildDirectory);
            }

            arguments.AddRange(trimmedNames);

            displayCommand = buildDirectory is null
                ? "yay -S --noconfirm --needed <packages>"
                : $"yay -S --noconfirm --needed --builddir {buildDirectory} <packages>";
        }

        yield return new PackageOperationProgress(
            PackageOperationKind.Update,
            PackageOperationStage.Preparing,
            5,
            trimmedNames.Length == 0
                ? "Preparing to update all packages."
                : $"Preparing to update {trimmedNames.Length} package(s).",
            displayCommand);

        if (await _privilegeService.TryElevateAsync(PackageOperationKind.Update, displayCommand, cancellationToken) is { } elevationOutcome)
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
