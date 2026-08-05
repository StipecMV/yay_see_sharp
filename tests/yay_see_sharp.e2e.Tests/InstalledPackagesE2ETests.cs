using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using yay_see_sharp.application.Views;

namespace yay_see_sharp.e2e.Tests;

public class InstalledPackagesE2ETests
{
    [Test]
    public async Task Minimized_build_job_chip_is_hidden_when_no_build_job_is_active()
    {
        // Regression: IsVisible="{Binding BuildJob.IsMinimized}" resolved to Avalonia's
        // UnsetValue while BuildJob was null, which falls back to IsVisible's own default
        // (true) rather than false — showing a stray "restore" chip in the corner even with no
        // install/uninstall/update running. Fixed with an explicit FallbackValue=False.
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;

            viewModel.SelectedNavigationItem = viewModel.NavigationItems[2];
            await viewModel.Installed.RefreshCommand.Execute();
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.Installed.BuildJob).IsNull();

            var installedView = window.GetVisualDescendants().OfType<InstalledPackagesView>().First();
            var restoreButton = installedView.GetVisualDescendants().OfType<Button>()
                .First(button => Equals(button.Content, "⤢"));
            var chipBorder = restoreButton.GetVisualAncestors().OfType<Border>().First();

            await Assert.That(chipBorder.IsVisible).IsFalse();
        });
    }
}
