using System.Collections.ObjectModel;
using Avalonia.Threading;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.Platform;

/// <summary>
/// UI-06/UI-22: in-app toast overlay, implementing the same <see cref="INotificationService"/>
/// abstraction every ViewModel already sends install/uninstall/update/error notifications through
/// — no ViewModel needed a new dependency to get toasts; AppBootstrapper just fans the existing
/// notification pipeline out to this in addition to (or instead of) the OS notifier.
/// </summary>
public sealed class ToastService : INotificationService
{
    private static readonly TimeSpan AutoDismissAfter = TimeSpan.FromSeconds(30);

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    public Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default)
    {
        // SendAsync can be called from any thread (background operations, the scheduler, ...) but
        // Toasts is bound directly to the UI — every mutation must happen on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastViewModel(title, body, level, Dismiss);
            Toasts.Add(toast);
            ScheduleAutoDismiss(toast);
        });

        return Task.CompletedTask;
    }

    private void Dismiss(ToastViewModel toast) => Toasts.Remove(toast);

    private void ScheduleAutoDismiss(ToastViewModel toast) => Task.Delay(AutoDismissAfter).ContinueWith(
        _ => Dispatcher.UIThread.Post(() => Toasts.Remove(toast)),
        TaskScheduler.Default);
}
