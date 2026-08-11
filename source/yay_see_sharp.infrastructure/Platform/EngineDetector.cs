using log4net;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Platform;

public sealed class EngineDetector : IEngineDetector
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(EngineDetector));
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
            Log.Info("Engine detection: yay found on PATH");
            return PackageManagerEngine.Yay;
        }

        if (IsOnPath("paru"))
        {
            Log.Info("Engine detection: paru found on PATH");
            return PackageManagerEngine.Paru;
        }

        Log.Info("Engine detection: neither yay nor paru found on PATH");
        return null;
    }

    private bool IsOnPath(string executableName) => _searchPaths
        .Select(directory => Path.Combine(directory, executableName))
        .Any(IsExecutableFile);

    /// <summary>A file merely existing on PATH isn't enough — a non-executable `yay` (e.g. downloaded but never chmod +x'd) must not be reported as usable, or the factory would select Real mode and then fail the moment it actually tries to run it.</summary>
    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
