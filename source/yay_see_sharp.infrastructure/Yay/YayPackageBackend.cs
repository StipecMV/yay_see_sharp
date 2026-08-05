using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public sealed partial class YayPackageBackend : IPackageBackend
{
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;
    private readonly IPrivilegeService? _privilegeService;

    public YayPackageBackend(
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        BackendInfo? info = null,
        IPrivilegeService? privilegeService = null)
    {
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
        Info = info ?? new BackendInfo(
            "arch",
            "Arch Linux",
            "yay",
            BackendMode.Real,
            true);
    }

    public BackendInfo Info { get; }

    /// <summary>
    /// Ensures sudo elevation before a privileged install/uninstall/update call. Returns null when
    /// elevation was granted (or no IPrivilegeService is configured, e.g. in tests) so the caller
    /// should proceed; otherwise returns the terminal progress to yield instead of running the
    /// command — cancelling the prompt cancels the operation, it never throws.
    /// </summary>
    private async Task<PackageOperationProgress?> TryElevateAsync(
        PackageOperationKind kind,
        string? displayCommand,
        CancellationToken cancellationToken)
    {
        if (_privilegeService is null)
        {
            return null;
        }

        var result = await _privilegeService.RequestElevationAsync(cancellationToken);
        return result switch
        {
            PrivilegeResult.Granted => null,
            PrivilegeResult.Cancelled => new PackageOperationProgress(
                kind, PackageOperationStage.Cancelled, 0, "Authentication cancelled.", displayCommand),
            PrivilegeResult.Failed => new PackageOperationProgress(
                kind, PackageOperationStage.Failed, 0, "Authentication failed.", displayCommand),
            _ => null,
        };
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

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Ss", query.Trim()]),
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
        if (string.IsNullOrWhiteSpace(packageName))
        {
            return null;
        }

        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Qi", packageName.Trim()]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return null;
        }

        return _outputParser.ParseInfo(result.CombinedText);
    }

    public async Task<PackageStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Qq"]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"yay statistics query failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        var installedCount = result.Output
            .Count(line => line.Kind == CommandOutputKind.StandardOutput && !string.IsNullOrWhiteSpace(line.Text));

        return new PackageStatistics(
            installedCount,
            0,
            0,
            0,
            0,
            0,
            0,
            null);
    }

    public async Task<IReadOnlyList<UpdateInfo>> GetUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", ["-Qu"]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"yay update check failed with exit code {result.ExitCode}: {result.CombinedText}");
        }

        return _outputParser.ParseUpdates(result.CombinedText);
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

        return _outputParser.ParseInstalled(result.CombinedText);
    }
}