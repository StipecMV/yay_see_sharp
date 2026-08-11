using log4net;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Privilege;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public sealed class YayPackageBackend : IPackageBackend
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(YayPackageBackend));
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;
    private readonly IPrivilegeService? _privilegeService;
    private readonly IPacmanQueryService _pacmanQueryService;
    private readonly IBuildDirectoryPolicy? _buildDirectoryPolicy;
    private readonly string _executable;

    private DateTimeOffset? _lastUpdateCheck;

    /// <param name="engine">The AUR-helper executable to drive: yay (default) or paru. Both
    /// share the same CLI surface for everything this backend issues (-Ss/-Si/-Sia/-Qi/-Q/-Qu/
    /// -S/-R/-Syu), so the backend is parameterized rather than duplicated (PARU-2026-08).</param>
    public YayPackageBackend(
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        BackendInfo? info = null,
        IPrivilegeService? privilegeService = null,
        IPacmanQueryService? pacmanQueryService = null,
        IBuildDirectoryPolicy? buildDirectoryPolicy = null,
        PackageManagerEngine? engine = null)
    {
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
        _pacmanQueryService = pacmanQueryService ?? new PacmanQueryService(commandRunner, outputParser);
        _buildDirectoryPolicy = buildDirectoryPolicy;
        _executable = engine == PackageManagerEngine.Paru ? "paru" : "yay";
        Info = info ?? new BackendInfo(
            "arch",
            "Arch Linux",
            _executable,
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
        var searchTask = _commandRunner.RunAsync(
            new CommandRequest(_executable, ["-Ss", "--", trimmedQuery]),
            cancellationToken: cancellationToken);

        // BUGFIX-2026-08: the "[installed]" marker yay prints next to search results is the
        // source of truth for the row state — but only when the marker actually appears in the
        // parsed line. Cross-checking against `pacman -Qq` (a fast, local query) guarantees the
        // Search screen's state always agrees with the Installed screen, regardless of yay
        // version/output quirks. If the local query fails, the parsed markers are kept as-is.
        var installedNamesTask = ReadInstalledNamesAsync(cancellationToken);

        await Task.WhenAll(searchTask, installedNamesTask);

        var result = searchTask.Result;
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{_executable} search failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var packages = _outputParser.ParseSearch(result.CombinedText);
        if (installedNamesTask.Result is { Count: > 0 } installedNames)
        {
            packages = packages
                .Select(package => installedNames.Contains(package.Name)
                    ? package with { State = PackageState.Installed }
                    : package)
                .ToArray();
        }

        var filtered = source is null
            ? packages
            : packages.Where(package => package.Source == source).ToArray();
        Log.Info($"Search '{trimmedQuery}' (source={source?.ToString() ?? "all"}): {filtered.Count} result(s)");
        return filtered;
    }

    private async Task<IReadOnlySet<string>?> ReadInstalledNamesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _commandRunner.RunAsync(
                new CommandRequest("pacman", ["-Qq"]),
                cancellationToken: cancellationToken);

            if (!result.Succeeded)
            {
                return null;
            }

            return result.CombinedText
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // A failed/broken local state query must never take down a search — the parsed
            // "[installed]" markers remain the fallback.
            return null;
        }
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
            new CommandRequest(_executable, ["-Qi", trimmed]),
            cancellationToken: cancellationToken);
        if (installed.Succeeded)
        {
            Log.Info($"Details for '{trimmed}' from local database (yay -Qi)");
            return _outputParser.ParseInfo(installed.CombinedText);
        }

        var official = await _commandRunner.RunAsync(
            new CommandRequest(_executable, ["-Si", trimmed]),
            cancellationToken: cancellationToken);
        if (official.Succeeded)
        {
            Log.Info($"Details for '{trimmed}' from sync database ({_executable} -Si)");
            return _outputParser.ParseInfo(official.CombinedText, PackageSource.Official);
        }

        var aur = await _commandRunner.RunAsync(
            new CommandRequest(_executable, ["-Sia", trimmed]),
            cancellationToken: cancellationToken);
        if (aur.Succeeded)
        {
            Log.Info($"Details for '{trimmed}' from AUR ({_executable} -Sia)");
            return _outputParser.ParseInfo(aur.CombinedText, PackageSource.Aur);
        }

        Log.Warn($"Details for '{trimmed}': no source had information (exit codes: -Qi={installed.ExitCode}, -Si={official.ExitCode}, -Sia={aur.ExitCode})");
        return null;
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
            new CommandRequest(_executable, ["-Qu"]),
            cancellationToken: cancellationToken);

        // yay -Qu (like pacman -Qu) exits 1 when there are simply no updates — a real, valid
        // "nothing to update", not a failed query. Only a non-zero exit that isn't this specific
        // shape counts as an actual failure. WasCancelled is excluded so a genuinely cancelled
        // check never gets misreported as "no updates" just because the killed process happened
        // to report exit code 1.
        if (result.ExitCode == 1 && !result.WasCancelled)
        {
            _lastUpdateCheck = DateTimeOffset.UtcNow;
            Log.Info("Update check: no updates available");
            return Array.Empty<UpdateInfo>();
        }

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{_executable} update check failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var foreignNames = await _pacmanQueryService.GetForeignPackageNamesAsync(cancellationToken);
        var confirmedAurNames = await _pacmanQueryService.GetConfirmedAurPackageNamesAsync(foreignNames, cancellationToken);
        _lastUpdateCheck = DateTimeOffset.UtcNow;
        var updates = _outputParser.ParseUpdates(result.CombinedText, foreignNames, confirmedAurNames);
        Log.Info($"Update check: {updates.Count} update(s) available");
        return updates;
    }

    public async Task<IReadOnlyList<PackageSummary>> GetInstalledPackagesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest(_executable, ["-Q"]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"{_executable} installed package query failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var foreignNames = await _pacmanQueryService.GetForeignPackageNamesAsync(cancellationToken);
        var confirmedAurNames = await _pacmanQueryService.GetConfirmedAurPackageNamesAsync(foreignNames, cancellationToken);
        var installed = _outputParser.ParseInstalled(result.CombinedText, foreignNames, confirmedAurNames);
        Log.Info($"Installed packages: {installed.Count} total ({confirmedAurNames.Count} confirmed AUR / {foreignNames.Count} foreign)");
        return installed;
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
            ? $"{_executable} --needed --noconfirm -S <package>"
            : $"{_executable} --needed --noconfirm --builddir {buildDirectory} -S <package>";

        Log.Info($"Install starting: {trimmedName}");
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
            new CommandRequest(_executable, arguments),
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
            var message = FormatFailure("Installation", result.ExitCode, result.CombinedText);
            Log.Warn($"Install failed: {trimmedName} — {message}");
            yield return new PackageOperationProgress(
                PackageOperationKind.Install,
                PackageOperationStage.Failed,
                0,
                message,
                displayCommand,
                result.CombinedText);
            yield break;
        }

        Log.Info($"Install completed: {trimmedName}");
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
        var displayCommand = $"{_executable} --noconfirm {removeFlag} <package>";

        Log.Info($"Uninstall starting: {packageName.Trim()} (removeOrphans={removeOrphans})");
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
            new CommandRequest(_executable, ["--noconfirm", removeFlag, packageName.Trim()]),
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
            var message = FormatFailure("Removal", result.ExitCode, result.CombinedText);
            Log.Warn($"Uninstall failed: {packageName.Trim()} — {message}");
            yield return new PackageOperationProgress(
                PackageOperationKind.Uninstall,
                PackageOperationStage.Failed,
                0,
                message,
                displayCommand,
                result.CombinedText);
            yield break;
        }

        Log.Info($"Uninstall completed: {packageName.Trim()}");
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
                ? $"{_executable} -Syu --noconfirm"
                : $"{_executable} -Syu --noconfirm --builddir {buildDirectory}";
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
                ? $"{_executable} -S --noconfirm --needed <packages>"
                : $"{_executable} -S --noconfirm --needed --builddir {buildDirectory} <packages>";
        }

        Log.Info(trimmedNames.Length == 0
            ? "Update starting: all packages"
            : $"Update starting: {trimmedNames.Length} package(s) ({string.Join(", ", trimmedNames.Take(5))}{(trimmedNames.Length > 5 ? ", ..." : "")})");
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
            new CommandRequest(_executable, arguments),
            cancellationToken: cancellationToken);

        if (result.WasCancelled)
        {
            Log.Warn("Update cancelled by the user");
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
            var message = FormatFailure("Update", result.ExitCode, result.CombinedText);
            Log.Warn($"Update failed — {message}");
            yield return new PackageOperationProgress(
                PackageOperationKind.Update,
                PackageOperationStage.Failed,
                0,
                message,
                displayCommand,
                result.CombinedText);
            yield break;
        }

        Log.Info("Update completed");
        yield return new PackageOperationProgress(
            PackageOperationKind.Update,
            PackageOperationStage.Completed,
            100,
            "Update completed.",
            displayCommand,
            result.CombinedText);
    }

    /// <summary>
    /// BUGFIX-2026-08: "Operation failed with exit code 1" told the user nothing about *why* the
    /// operation failed. The failure message now appends the last few meaningful lines of the
    /// process output (e.g. "error: target not found: foo" or a makepkg error), which is what
    /// the toast surfaces. The full output stays available on the operation (BuildJob modal).
    /// </summary>
    private static string FormatFailure(string action, int exitCode, string? output)
    {
        var tail = SummarizeOutput(output);
        return tail.Length == 0
            ? $"{action} failed with exit code {exitCode}."
            : $"{action} failed with exit code {exitCode}.\n\n{tail}";
    }

    private static string SummarizeOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return string.Empty;
        }

        var meaningful = output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        const int maxLines = 10;
        return string.Join("\n", meaningful.TakeLast(maxLines));
    }

}
