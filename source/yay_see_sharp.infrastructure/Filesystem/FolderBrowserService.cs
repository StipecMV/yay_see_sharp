using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Filesystem;

public sealed class FolderBrowserService : IFolderBrowserService
{
    public IReadOnlyList<string> GetSubdirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path)
                .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public string GetParentPath(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? path;
}
