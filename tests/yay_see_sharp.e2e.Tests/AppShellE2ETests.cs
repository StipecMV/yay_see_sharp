using System.Reactive.Linq;
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
    public async Task Sidebar_renders_one_realized_list_box_item_per_main_navigation_entry_plus_a_settings_button()
    {
        // Regression coverage: the NavListBox ControlTheme used to omit a Template setter, so the
        // ListBox had ItemsSource bound (ItemCount correct) but no ItemsPresenter/panel at all —
        // zero ListBoxItems ever got realized and the sidebar rendered completely empty. Matching
        // on ItemsSource alone (as the navigation test below does) doesn't catch that class of
        // bug; only actually walking the visual tree for realized item containers does.
        //
        // UI-14: Settings lives in its own bottom-pinned Button (not a second ListBox — see
        // MainWindowViewModel.IsSettingsSelected for why), so it's checked separately here.
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            var mainSidebar = window.GetVisualDescendants().OfType<ListBox>()
                .First(box => ReferenceEquals(box.ItemsSource, viewModel.MainNavigationItems));

            var realizedItems = mainSidebar.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            await Assert.That(realizedItems.Count).IsEqualTo(viewModel.MainNavigationItems.Count);
            await Assert.That(realizedItems.All(item => item.Bounds.Height > 0)).IsTrue();

            var settingsButton = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Classes.Contains("navItem"));
            await Assert.That(settingsButton.Bounds.Height).IsGreaterThan(0);
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

            var mainSidebar = window.GetVisualDescendants().OfType<ListBox>()
                .First(box => ReferenceEquals(box.ItemsSource, viewModel.MainNavigationItems));

            mainSidebar.SelectedItem = viewModel.NavigationItems[1];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Search);
            await Assert.That(window.GetVisualDescendants().OfType<SearchView>().FirstOrDefault()).IsNotNull();

            mainSidebar.SelectedItem = viewModel.NavigationItems[2];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Installed);
            await Assert.That(window.GetVisualDescendants().OfType<InstalledPackagesView>().FirstOrDefault()).IsNotNull();

            await viewModel.SelectSettingsCommand.Execute();
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Settings);
            await Assert.That(window.GetVisualDescendants().OfType<SettingsView>().FirstOrDefault()).IsNotNull();

            mainSidebar.SelectedItem = viewModel.NavigationItems[0];
            AvaloniaUiTest.Pump();
            await Assert.That(viewModel.CurrentPage).IsEqualTo(viewModel.Dashboard);
            await Assert.That(window.GetVisualDescendants().OfType<DashboardView>().FirstOrDefault()).IsNotNull();
        });
    }
}
