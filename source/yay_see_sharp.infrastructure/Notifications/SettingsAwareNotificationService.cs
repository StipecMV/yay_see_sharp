using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Notifications;

/// <summary>Gates an inner INotificationService on the live Settings.NotificationsEnabled toggle, so callers don't have to check it themselves at every call site.</summary>
public sealed class SettingsAwareNotificationService : INotificationService
{
    private readonly INotificationService _inner;
    private readonly INotificationSettings _settings;

    public SettingsAwareNotificationService(INotificationService inner, INotificationSettings settings)
    {
        _inner = inner;
        _settings = settings;
    }

    public Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default) => _settings.NotificationsEnabled
        ? _inner.SendAsync(title, body, level, cancellationToken)
        : Task.CompletedTask;
}
