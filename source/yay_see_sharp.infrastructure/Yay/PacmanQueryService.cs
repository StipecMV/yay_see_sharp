using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Yay;

public sealed class PacmanQueryService : IPacmanQueryService
{
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;

    public PacmanQueryService(ICommandRunner commandRunner, IYayOutputParser? outputParser = null)
    {
        _commandRunner = commandRunner;
        _outputParser = outputParser ?? new YayOutputParser();
    }

    public async Task<PackageStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var installedResult = await _commandRunner.RunAsync(
            new CommandRequest("pacman", ["-Qq"]),
            cancellationToken: cancellationToken);

        if (!installedResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"pacman installed-package query failed with exit code {installedResult.ExitCode}: {installedResult.CombinedText}");
        }

        var installedCount = CountNonEmptyLines(installedResult.CombinedText);
        var explicitCount = await InterpretFilteredCountAsync(["-Qe"], cancellationToken);
        var dependencyCount = await InterpretFilteredCountAsync(["-Qd"], cancellationToken);
        var orphanCount = await InterpretFilteredCountAsync(["-Qdt"], cancellationToken);
        var aurCount = await GetConfirmedAurCountAsync(cancellationToken);
        var updatesAvailable = await InterpretFilteredCountAsync(["-Qu"], cancellationToken);
        var installedSizeBytes = await GetInstalledSizeBytesAsync(cancellationToken);

        return new PackageStatistics(
            installedCount,
            explicitCount,
            dependencyCount,
            aurCount,
            updatesAvailable,
            installedSizeBytes,
            orphanCount,
            LastUpdateCheck: null);
    }

    public async Task<IReadOnlySet<string>> GetForeignPackageNamesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("pacman", ["-Qm"]),
            cancellationToken: cancellationToken);

        return result.Succeeded ? ExtractNames(result.CombinedText) : new HashSet<string>();
    }

    public async Task<IReadOnlySet<string>> GetConfirmedAurPackageNamesAsync(
        IReadOnlySet<string> foreignPackageNames, CancellationToken cancellationToken = default)
    {
        if (foreignPackageNames.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // yay's `-Si` transparently merges pacman sync-db info with AUR RPC info, so a bulk query
        // against just the foreign (not-in-any-configured-repo) names tells us which of them yay
        // can actually resolve against AUR — that's the confirmation. Any name yay can't resolve
        // stays classified as Foreign rather than being assumed to be AUR.
        var arguments = new List<string> { "-Si", "--" };
        arguments.AddRange(foreignPackageNames);
        var result = await _commandRunner.RunAsync(
            new CommandRequest("yay", arguments),
            cancellationToken: cancellationToken);

        return result.Succeeded
            ? _outputParser.ParseAurConfirmedNames(result.CombinedText)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Null when the underlying `pacman -Qm` query itself failed (unknown, not zero); a confirmation query failure is not fatal — it degrades to 0 confirmed rather than unknown, since "no AUR packages confirmed" is still a valid, honest answer.</summary>
    private async Task<int?> GetConfirmedAurCountAsync(CancellationToken cancellationToken)
    {
        var foreignResult = await _commandRunner.RunAsync(
            new CommandRequest("pacman", ["-Qm"]),
            cancellationToken: cancellationToken);

        if (!foreignResult.Succeeded)
        {
            return null;
        }

        var foreignNames = ExtractNames(foreignResult.CombinedText);
        var confirmed = await GetConfirmedAurPackageNamesAsync(foreignNames, cancellationToken);
        return confirmed.Count;
    }

    private async Task<long?> GetInstalledSizeBytesAsync(CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("pacman", ["-Qi"]),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return null;
        }

        long total = 0;
        foreach (var line in result.CombinedText.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (!key.Equals("Installed Size", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            total += PacmanSizeParser.ParseToBytes(line[(separator + 1)..].Trim());
        }

        return total;
    }

    /// <summary>
    /// Runs a `pacman -Q*` filter query and interprets its exit code. These filters (`-Qe`,
    /// `-Qd`, `-Qm`, `-Qdt`, `-Qu`) exit 1 with empty output when there are simply zero matches —
    /// a real, valid "0", not a failed query — so only a non-zero exit *with* output, or any
    /// other unexpected shape, is treated as an unknown/failed result.
    /// </summary>
    private async Task<int?> InterpretFilteredCountAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            new CommandRequest("pacman", arguments),
            cancellationToken: cancellationToken);

        if (result.ExitCode == 0)
        {
            return CountNonEmptyLines(result.CombinedText);
        }

        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.CombinedText))
        {
            return 0;
        }

        return null;
    }

    private static HashSet<string> ExtractNames(string output) => output
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .Select(line => line.Split(' ', 2)[0])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static int CountNonEmptyLines(string text) => text
        .Split('\n')
        .Count(line => !string.IsNullOrWhiteSpace(line));
}
