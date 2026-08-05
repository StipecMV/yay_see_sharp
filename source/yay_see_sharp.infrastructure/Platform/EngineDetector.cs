using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Platform;

public interface IEngineDetector
{
    PackageManagerEngine? Detect();
}

public sealed class EngineDetector : IEngineDetector
{
    private readonly IReadOnlyList<string> _searchPaths;

    public EngineDetector(string? pathEnvironmentVariable = null)
    {
        var value = pathEnvironmentVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        _searchPaths = value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    public PackageManagerEngine? Detect()
    {
        if (IsOnPath("yay"))
        {
            return PackageManagerEngine.Yay;
        }

        if (IsOnPath("paru"))
        {
            return PackageManagerEngine.Paru;
        }

        return null;
    }

    private bool IsOnPath(string executableName) => _searchPaths
        .Any(directory => File.Exists(Path.Combine(directory, executableName)));
}
