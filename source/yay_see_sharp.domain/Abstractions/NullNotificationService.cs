namespace yay_see_sharp.domain.Abstractions;

/// <summary>
/// No-op INotificationService, safe as a ViewModel constructor default: it performs no I/O of any
/// kind (no process, no D-Bus, nothing platform-specific), so — unlike the real
/// NotifySendNotificationService — it belongs next to the abstraction in the domain project rather
/// than infrastructure. A ViewModel referencing this is not a composition-root violation; one
/// referencing a real Infrastructure notifier would be.
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    public static readonly NullNotificationService Instance = new();

    public Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
