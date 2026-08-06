using System.Text.RegularExpressions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public sealed class YayOutputParser : IYayOutputParser
{
    private static readonly Regex SearchHeader = new(
        @"^(?<repository>[^/\s]+)/(?<name>\S+)\s+(?<version>\S+)(?:\s+\(\+(?<votes>\d+)\s+[\d.]+\))?(?:\s+\[(?<status>[^\]]+)\])?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UpdateLine = new(
        @"^(?<name>\S+)\s+(?<current>\S+)\s+->\s+(?<available>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InstalledLine = new(
        @"^(?<name>\S+)\s+(?<version>\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<PackageSummary> ParseSearch(string output)
    {
        var lines = output.Split('\n');
        var packages = new List<PackageSummary>();

        for (var index = 0; index < lines.Length; index++)
        {
            var match = SearchHeader.Match(lines[index].Trim());
            if (!match.Success)
            {
                continue;
            }

            var repository = match.Groups["repository"].Value;
            var state = ParseState(match.Groups["status"].Value);
            var votes = match.Groups["votes"].Success ? int.Parse(match.Groups["votes"].Value) : (int?)null;
            var description = string.Empty;
            if (index + 1 < lines.Length && lines[index + 1].Trim().Length > 0 &&
                char.IsWhiteSpace(lines[index + 1], 0))
            {
                description = lines[++index].Trim();
            }

            packages.Add(new PackageSummary(
                match.Groups["name"].Value,
                match.Groups["version"].Value,
                description,
                IsAurRepository(repository) ? PackageSource.Aur : PackageSource.Official,
                0,
                state,
                Votes: votes));
        }

        return packages;
    }

    public PackageDetails? ParseInfo(string output, PackageSource? sourceHint = null)
    {
        var fields = ParseFields(output);
        if (!fields.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var repository = fields.GetValueOrDefault("Repository", string.Empty);
        var version = fields.GetValueOrDefault("Version", string.Empty);
        var description = fields.GetValueOrDefault("Description", string.Empty);
        var installedSize = PacmanSizeParser.ParseToBytes(fields.GetValueOrDefault("Installed Size", string.Empty));
        var state = fields.ContainsKey("Install Date")
            ? PackageState.Installed
            : PackageState.NotInstalled;
        // The Repository field is authoritative when present (covers -Qi/-Si); a -Sia (AUR
        // sync-info) response may omit it entirely, so the caller's sourceHint — which query path
        // actually succeeded — is the fallback rather than silently defaulting to Official.
        var source = repository.Length > 0
            ? (IsAurRepository(repository) ? PackageSource.Aur : PackageSource.Official)
            : sourceHint ?? PackageSource.Official;
        var summary = new PackageSummary(name, version, description, source, installedSize, state);
        var dependencies = ParseList(fields.GetValueOrDefault("Depends On", string.Empty))
            .Select(dependency => new PackageDependency(dependency, string.Empty, false))
            .ToArray();
        var homepage = fields.TryGetValue("URL", out var parsedHomepage) ? parsedHomepage : null;
        var packager = fields.TryGetValue("Packager", out var parsedPackager) ? parsedPackager : null;

        return new PackageDetails(summary, packager, homepage, dependencies, []);
    }

    public IReadOnlyList<UpdateInfo> ParseUpdates(
        string output,
        IReadOnlySet<string>? foreignPackageNames = null,
        IReadOnlySet<string>? confirmedAurPackageNames = null)
    {
        var updates = new List<UpdateInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = UpdateLine.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            updates.Add(new UpdateInfo(
                name,
                match.Groups["current"].Value,
                match.Groups["available"].Value,
                ClassifySource(name, foreignPackageNames, confirmedAurPackageNames),
                0));
        }

        return updates;
    }

    public IReadOnlyList<PackageSummary> ParseInstalled(
        string output,
        IReadOnlySet<string>? foreignPackageNames = null,
        IReadOnlySet<string>? confirmedAurPackageNames = null)
    {
        var packages = new List<PackageSummary>();
        foreach (var line in output.Split('\n'))
        {
            var match = InstalledLine.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            packages.Add(new PackageSummary(
                name,
                match.Groups["version"].Value,
                string.Empty,
                ClassifySource(name, foreignPackageNames, confirmedAurPackageNames),
                0,
                PackageState.Installed));
        }

        return packages;
    }

    public IReadOnlySet<string> ParseAurConfirmedNames(string output)
    {
        var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? pendingName = null;
        string? pendingRepository = null;

        void Flush()
        {
            if (pendingName is not null && pendingRepository is not null && IsAurRepository(pendingRepository))
            {
                confirmed.Add(pendingName);
            }

            pendingName = null;
            pendingRepository = null;
        }

        foreach (var line in output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                pendingName = value;
            }
            else if (key.Equals("Repository", StringComparison.OrdinalIgnoreCase))
            {
                pendingRepository = value;
            }
        }

        Flush();
        return confirmed;
    }

    /// <summary>
    /// Source-classification rule shared by ParseInstalled/ParseUpdates: a name confirmed against
    /// AUR metadata (<paramref name="confirmedAurPackageNames"/>) is AUR; a name merely present in
    /// the foreign-package set (from `pacman -Qm`, not-in-any-configured-repo) but not confirmed is
    /// Foreign — never assumed to be AUR just because it's out-of-repo; everything else is Official.
    /// </summary>
    private static PackageSource ClassifySource(
        string packageName,
        IReadOnlySet<string>? foreignPackageNames,
        IReadOnlySet<string>? confirmedAurPackageNames)
    {
        if (confirmedAurPackageNames is not null && confirmedAurPackageNames.Contains(packageName))
        {
            return PackageSource.Aur;
        }

        if (foreignPackageNames is not null && foreignPackageNames.Contains(packageName))
        {
            return PackageSource.Foreign;
        }

        return PackageSource.Official;
    }

    private static Dictionary<string, string> ParseFields(string output)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            fields[key] = value;
        }

        return fields;
    }

    private static IEnumerable<string> ParseList(string value) => value
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(item => item is not "None" and not "(None)");

    private static PackageState ParseState(string status) =>
        status.Contains("installed", StringComparison.OrdinalIgnoreCase)
            ? PackageState.Installed
            : PackageState.NotInstalled;

    private static bool IsAurRepository(string repository) =>
        repository.Equals("aur", StringComparison.OrdinalIgnoreCase) ||
        repository.Contains("aur", StringComparison.OrdinalIgnoreCase);
}
