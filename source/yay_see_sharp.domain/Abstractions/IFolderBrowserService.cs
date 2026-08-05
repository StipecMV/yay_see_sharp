namespace yay_see_sharp.domain.Abstractions;

/// <summary>Filesystem access needed by the folder browser modal, kept behind an interface so FolderBrowserViewModel never touches Directory/File directly.</summary>
public interface IFolderBrowserService
{
    IReadOnlyList<string> GetSubdirectories(string path);

    bool DirectoryExists(string path);

    string GetParentPath(string path);
}
