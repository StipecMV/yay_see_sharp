using System.Reactive.Linq;
using TUnit.Core;
using yay_see_sharp.infrastructure.Filesystem;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class FolderBrowserFilesystemIntegrationTests
{
    [Test]
    public async Task Browsing_tmp_lists_at_least_one_real_subdirectory_and_can_navigate_into_and_back_out_of_it()
    {
        // /tmp's own contents vary by host, so create one guaranteed subdirectory rather than
        // assuming anything about what else happens to be there.
        var probeDirectory = Path.Combine(Path.GetTempPath(), "yss-folder-browser-probe-" + Guid.NewGuid());
        Directory.CreateDirectory(probeDirectory);

        try
        {
            var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetTempPath());
            var viewModel = new FolderBrowserViewModel(tempRoot, new LocalizationService("en"), new FolderBrowserService());

            await Assert.That(viewModel.Children.Count).IsGreaterThan(0);

            var probeEntry = viewModel.Children.FirstOrDefault(child => child.FullPath == probeDirectory);
            await Assert.That(probeEntry).IsNotNull();

            await viewModel.OpenEntryCommand.Execute(probeEntry!);
            await Assert.That(viewModel.CurrentPath).IsEqualTo(probeDirectory);
            await Assert.That(viewModel.Children.Count).IsEqualTo(0);

            await viewModel.NavigateCommand.Execute(tempRoot);
            await Assert.That(viewModel.CurrentPath).IsEqualTo(tempRoot);
            await Assert.That(viewModel.Children.Any(child => child.FullPath == probeDirectory)).IsTrue();
        }
        finally
        {
            Directory.Delete(probeDirectory, recursive: true);
        }
    }
}
