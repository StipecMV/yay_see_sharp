using log4net;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Platform;

public sealed record DistributionSnapshot(
    string Id,
    string Name,
    string DesktopEnvironment,
    string SessionType);

public interface IDistributionDetector
{
    DistributionSnapshot Detect();

    /// <param name="yayAvailable">Whether `yay` was actually found on PATH — the caller (PackageBackendFactory) is the single place that resolves this via IEngineDetector, so distribution identity and engine availability never diverge.</param>
    BackendInfo CreateBackendInfo(DistributionSnapshot distribution, bool yayAvailable);
}

public sealed class LinuxDistributionDetector : IDistributionDetector
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(LinuxDistributionDetector));
    private readonly string _osReleasePath;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public LinuxDistributionDetector(
        string osReleasePath = "/etc/os-release",
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _osReleasePath = osReleasePath;
        _environment = environment ?? Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (Key: entry.Key as string, Value: entry.Value as string))
            .Where(entry => entry.Key is not null && entry.Value is not null)
            .ToDictionary(entry => entry.Key!, entry => entry.Value!);
    }

    public DistributionSnapshot Detect()
    {
        var values = File.Exists(_osReleasePath)
            ? File.ReadAllLines(_osReleasePath)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .Select(parts => (Key: parts[0], Value: Unquote(parts[1])))
                .ToDictionary(parts => parts.Key, parts => parts.Value)
            : new Dictionary<string, string>();

        var id = values.GetValueOrDefault("ID", "unknown").ToLowerInvariant();
        var name = values.GetValueOrDefault("PRETTY_NAME", id);
        var desktop = FirstEnvironmentValue("XDG_CURRENT_DESKTOP", "unknown");
        var session = FirstEnvironmentValue("XDG_SESSION_TYPE", "unknown");
        var snapshot = new DistributionSnapshot(id, name, desktop, session);
        Log.Info($"Distribution detected: id={snapshot.Id} name={snapshot.Name} desktop={snapshot.DesktopEnvironment} session={snapshot.SessionType}");
        return snapshot;
    }

    public BackendInfo CreateBackendInfo(DistributionSnapshot distribution, bool yayAvailable)
    {
        var isArch = distribution.Id is "arch" or "cachyos";
        if (!isArch)
        {
            return new BackendInfo(
                distribution.Id,
                distribution.Name,
                "demo",
                BackendMode.Demo,
                false,
                "Real yay backend is limited to Arch Linux and CachyOS; using realistic Demo mode.");
        }

        return yayAvailable
            ? new BackendInfo(distribution.Id, distribution.Name, "yay", BackendMode.Real, true)
            : new BackendInfo(
                distribution.Id,
                distribution.Name,
                "demo",
                BackendMode.Unavailable,
                false,
                "yay was not found on PATH. Install it to use Real mode, or continue in Demo mode.");
    }

    private string FirstEnvironmentValue(params string[] names) => names
        .Select(name => _environment.GetValueOrDefault(name))
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "unknown";

    private static string Unquote(string value) => value.Trim().Trim('"', '\'');
}