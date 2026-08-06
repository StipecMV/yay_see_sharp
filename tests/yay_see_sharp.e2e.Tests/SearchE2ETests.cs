using System.Reactive.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.e2e.Tests;

public class SearchE2ETests
{
    [Test]
    public async Task Entering_a_search_query_renders_matching_results_from_a_mocked_backend()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var backend = new Mock<IPackageBackend>();
            backend.Setup(b => b.Info).Returns(new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true));
            backend.Setup(b => b.GetStatisticsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PackageStatistics(0, 0, 0, 0, 0, 0, 0, null));
            backend.Setup(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<UpdateInfo>)[]);
            backend.Setup(b => b.SearchAsync("firefox", null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<PackageSummary>)
                [
                    new PackageSummary("firefox", "129.0", "Fast, private and open web browser.", PackageSource.Official, 0, PackageState.NotInstalled),
                ]);

            var (window, viewModel, _) = TestShellFactory.Create(backend.Object);
            await viewModel.Dashboard.InitialLoadTask;

            viewModel.SelectedNavigationItem = viewModel.NavigationItems[1];
            AvaloniaUiTest.Pump();

            viewModel.Search.Query = "firefox";
            await viewModel.Search.SearchCommand.Execute();
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.Search.Results.Count).IsEqualTo(1);

            var resultsList = window.GetVisualDescendants().OfType<ItemsControl>()
                .First(control => ReferenceEquals(control.ItemsSource, viewModel.Search.Results));
            AvaloniaUiTest.Pump();

            var renderedText = resultsList.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text)
                .Where(t => t is not null)
                .ToArray();
            await Assert.That(renderedText.Any(t => t!.Contains("firefox"))).IsTrue();

            // AtLeastOnce, not Once: UI-07's live-search pipeline (debounced Query change) can also
            // fire its own SearchAsync("firefox", ...) call independently of this test's explicit
            // SearchCommand.Execute() — both are legitimate, so the count itself isn't a contract.
            backend.Verify(b => b.SearchAsync("firefox", null, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        });
    }
}
