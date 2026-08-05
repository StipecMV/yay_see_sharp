using Avalonia.Controls;
using Avalonia.VisualTree;
using yay_see_sharp.application.Views;

namespace yay_see_sharp.e2e.Tests;

public class AppShellE2ETests
{
    [Test]
    public async Task App_launches_headless_and_shows_the_dashboard()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Dashboard);

            var dashboardView = window.GetVisualDescendants().OfType<DashboardView>().FirstOrDefault();
            await Assert.That(dashboardView).IsNotNull();
        });
    }

    [Test]
    public async Task Sidebar_renders_one_realized_list_box_item_per_navigation_entry()
    {
        // Regression coverage: the NavListBox ControlTheme used to omit a Template setter, so the
        // ListBox had ItemsSource bound (ItemCount correct) but no ItemsPresenter/panel at all —
        // zero ListBoxItems ever got realized and the sidebar rendered completely empty. Matching
        // on ItemsSource alone (as the navigation test below does) doesn't catch that class of
        // bug; only actually walking the visual tree for realized item containers does.
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            var sidebar = window.GetVisualDescendants().OfType<ListBox>()
                .First(box => ReferenceEquals(box.ItemsSource, viewModel.NavigationItems));

            var realizedItems = sidebar.GetVisualDescendants().OfType<ListBoxItem>().ToList();

            await Assert.That(realizedItems.Count).IsEqualTo(viewModel.NavigationItems.Count);
            await Assert.That(realizedItems.All(item => item.Bounds.Height > 0)).IsTrue();
        });
    }

    [Test]
    public async Task Sidebar_navigation_switches_the_rendered_page_across_all_four_sections()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            var sidebar = window.GetVisualDescendants().OfType<ListBox>()
                .First(box => ReferenceEquals(box.ItemsSource, viewModel.NavigationItems));

            sidebar.SelectedItem = viewModel.NavigationItems[1];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Search);
            await Assert.That(window.GetVisualDescendants().OfType<SearchView>().FirstOrDefault()).IsNotNull();

            sidebar.SelectedItem = viewModel.NavigationItems[2];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Installed);
            await Assert.That(window.GetVisualDescendants().OfType<InstalledPackagesView>().FirstOrDefault()).IsNotNull();

            sidebar.SelectedItem = viewModel.NavigationItems[3];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Settings);
            await Assert.That(window.GetVisualDescendants().OfType<SettingsView>().FirstOrDefault()).IsNotNull();

            sidebar.SelectedItem = viewModel.NavigationItems[0];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Dashboard);
            await Assert.That(window.GetVisualDescendants().OfType<DashboardView>().FirstOrDefault()).IsNotNull();
        });
    }
}
