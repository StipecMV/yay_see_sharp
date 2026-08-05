using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Notifications;

/// <summary>No-op fallback used when a view model isn't given a real INotificationService (mainly tests), so nothing ever shells out to notify-send unless explicitly wired.</summary>
public sealed class NullNotificationService : INotificationService
{
    public static readonly NullNotificationService Instance = new();

    public Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
