using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace yay_see_sharp.e2e.Tests;

public class DashboardE2ETests
{
    [Test]
    public async Task Clicking_dismiss_on_the_dashboard_notification_banner_hides_it()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            await viewModel.Dashboard.NotifyUpdatesAvailableAsync(3);
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.Dashboard.HasNotification).IsTrue();

            var dismissButton = window.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.Command, viewModel.Dashboard.DismissNotificationCommand));
            var bounds = dismissButton.Bounds;
            var center = dismissButton.TranslatePoint(new Point(bounds.Width / 2, bounds.Height / 2), window)
                ?? throw new InvalidOperationException("Dismiss button has no layout bounds yet.");

            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.Dashboard.HasNotification).IsFalse();
            await Assert.That(viewModel.Dashboard.NotificationMessage).IsNull();
        });
    }
}
