using System.Globalization;
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

    public PackageDetails? ParseInfo(string output)
    {
        var fields = ParseFields(output);
        if (!fields.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var repository = fields.GetValueOrDefault("Repository", string.Empty);
        var version = fields.GetValueOrDefault("Version", string.Empty);
        var description = fields.GetValueOrDefault("Description", string.Empty);
        var installedSize = ParseSize(fields.GetValueOrDefault("Installed Size", string.Empty));
        var state = fields.ContainsKey("Install Date")
            ? PackageState.Installed
            : PackageState.NotInstalled;
        var source = IsAurRepository(repository) ? PackageSource.Aur : PackageSource.Official;
        var summary = new PackageSummary(name, version, description, source, installedSize, state);
        var dependencies = ParseList(fields.GetValueOrDefault("Depends On", string.Empty))
            .Select(dependency => new PackageDependency(dependency, string.Empty, false))
            .ToArray();
        var homepage = fields.TryGetValue("URL", out var parsedHomepage) ? parsedHomepage : null;
        var packager = fields.TryGetValue("Packager", out var parsedPackager) ? parsedPackager : null;

        return new PackageDetails(summary, packager, homepage, dependencies, []);
    }

    public IReadOnlyList<UpdateInfo> ParseUpdates(string output)
    {
        var updates = new List<UpdateInfo>();
        foreach (var line in output.Split('\n'))
        {
            var match = UpdateLine.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            updates.Add(new UpdateInfo(
                match.Groups["name"].Value,
                match.Groups["current"].Value,
                match.Groups["available"].Value,
                PackageSource.Official,
                0));
        }

        return updates;
    }

    public IReadOnlyList<PackageSummary> ParseInstalled(string output)
    {
        var packages = new List<PackageSummary>();
        foreach (var line in output.Split('\n'))
        {
            var match = InstalledLine.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            packages.Add(new PackageSummary(
                match.Groups["name"].Value,
                match.Groups["version"].Value,
                string.Empty,
                PackageSource.Official,
                0,
                PackageState.Installed));
        }

        return packages;
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

    private static long ParseSize(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return 0;
        }

        var multiplier = parts[1].ToUpperInvariant() switch
        {
            "KIB" => 1024d,
            "MIB" => 1024d * 1024d,
            "GIB" => 1024d * 1024d * 1024d,
            _ => 1d,
        };
        return (long)(number * multiplier);
    }
}
