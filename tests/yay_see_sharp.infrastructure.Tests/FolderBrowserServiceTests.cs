using System.IO;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Filesystem;

public class FolderBrowserServiceTests
{
    [Test]
    public async Task GetSubdirectories_returns_immediate_children_in_alphabetical_order()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "zeta"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "alpha"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "not-a-directory.txt"), "noise");
            var service = new FolderBrowserService();

            var subdirectories = service.GetSubdirectories(root.FullName);

            await Assert.That(subdirectories.Count).IsEqualTo(2);
            await Assert.That(subdirectories[0]).EndsWith("alpha");
            await Assert.That(subdirectories[1]).EndsWith("zeta");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task GetSubdirectories_returns_empty_for_a_path_it_cannot_read()
    {
        var service = new FolderBrowserService();

        var subdirectories = service.GetSubdirectories(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Path.GetRandomFileName()));

        await Assert.That(subdirectories.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DirectoryExists_reflects_the_real_filesystem()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            var service = new FolderBrowserService();

            await Assert.That(service.DirectoryExists(root.FullName)).IsTrue();
            await Assert.That(service.DirectoryExists(Path.Combine(root.FullName, "missing"))).IsFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Test]
    public async Task GetParentPath_strips_a_trailing_separator_before_resolving_the_parent()
    {
        var service = new FolderBrowserService();

        var parent = service.GetParentPath(Path.Combine(Path.GetTempPath(), "a", "b") + Path.DirectorySeparatorChar);

        await Assert.That(parent).IsEqualTo(Path.Combine(Path.GetTempPath(), "a").TrimEnd(Path.DirectorySeparatorChar));
    }
}
