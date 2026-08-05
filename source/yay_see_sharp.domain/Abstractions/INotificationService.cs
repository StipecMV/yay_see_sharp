namespace yay_see_sharp.domain.Abstractions;

public enum NotificationLevel
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Desktop notifications. Implementations must never let a failure to notify propagate — see NotifySendNotificationService.</summary>
public interface INotificationService
{
    Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default);
}
