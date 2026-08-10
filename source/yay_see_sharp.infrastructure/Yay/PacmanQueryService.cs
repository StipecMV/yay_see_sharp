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
        //
        // BUGFIX-2026-08: one giant `yay -Si <all foreign names>` call is fragile — a single
        // unresolvable name can fail the whole query (and previously the entire AUR count
        // degraded to 0/"unknown"). Query in bounded chunks instead, merge whatever each chunk
        // confirmed, and let a chunk failure degrade only that chunk: 0 confirmed from a failed
        // chunk is still an honest "not confirmed", while the rest of the system keeps its data.
        //
        // NOTE (2026-08, follow-up): `yay -Si` exits non-zero when ANY requested name is
        // unresolvable (e.g. a foreign package that isn't in the AUR, like a hand-built tool),
        // but it still prints the info blocks for every name it DID resolve. Relying on
        // result.Succeeded to decide whether to parse meant one bad name silently discarded the
        // whole chunk — AUR packages ended up classified as Foreign, so the Installed AUR filter
        // showed 0 packages and the Dashboard AUR count showed 0 even on systems with many real
        // AUR packages installed. The output is now parsed regardless of the exit code; a chunk
        // that yielded no resolvable blocks simply contributes nothing.
        const int chunkSize = 20;
        const int maxConcurrency = 4;
        var chunks = foreignPackageNames
            .Chunk(chunkSize)
            .Select(chunk => (IReadOnlyList<string>)chunk)
            .ToArray();

        using var gate = new SemaphoreSlim(maxConcurrency);
        var results = await Task.WhenAll(chunks.Select(async chunk =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await _commandRunner.RunAsync(
                    new CommandRequest("yay", ["-Si", "--", .. chunk]),
                    cancellationToken: cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }));

        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            // Defensive: parser implementations (or mocks) may return null instead of an
            // empty set — never crash the whole AUR count over a missing confirmation list.
            var parsed = _outputParser.ParseAurConfirmedNames(result.CombinedText);
            if (parsed is not null)
            {
                confirmed.UnionWith(parsed);
            }
        }

        return confirmed;
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
