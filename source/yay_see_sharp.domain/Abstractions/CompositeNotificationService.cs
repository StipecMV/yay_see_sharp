namespace yay_see_sharp.domain.Abstractions;

/// <summary>
/// Fans a single notification out to multiple <see cref="INotificationService"/> instances — e.g.
/// the OS desktop notifier and the in-app toast overlay both firing from the same call site. Has
/// no I/O of its own (same rationale as <see cref="NullNotificationService"/> for living in domain
/// rather than infrastructure): it only delegates.
/// </summary>
public sealed class CompositeNotificationService : INotificationService
{
    private readonly IReadOnlyList<INotificationService> _services;

    public CompositeNotificationService(params IReadOnlyList<INotificationService> services)
    {
        _services = services;
    }

    public async Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default)
    {
        foreach (var service in _services)
        {
            await service.SendAsync(title, body, level, cancellationToken);
        }
    }
}
