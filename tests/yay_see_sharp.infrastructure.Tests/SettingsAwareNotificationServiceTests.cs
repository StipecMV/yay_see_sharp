using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Notifications;

namespace yay_see_sharp.infrastructure.Tests;

public class SettingsAwareNotificationServiceTests
{
    private sealed class FakeSettings(bool notificationsEnabled) : INotificationSettings
    {
        public bool NotificationsEnabled { get; } = notificationsEnabled;
    }

    [Test]
    public async Task Send_delegates_to_the_inner_service_when_notifications_are_enabled()
    {
        var inner = new Mock<INotificationService>();
        var service = new SettingsAwareNotificationService(inner.Object, new FakeSettings(notificationsEnabled: true));

        await service.SendAsync("Title", "Body", NotificationLevel.Success);

        inner.Verify(i => i.SendAsync("Title", "Body", NotificationLevel.Success, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Send_is_a_no_op_when_notifications_are_disabled()
    {
        var inner = new Mock<INotificationService>();
        var service = new SettingsAwareNotificationService(inner.Object, new FakeSettings(notificationsEnabled: false));

        await service.SendAsync("Title", "Body");

        inner.Verify(i => i.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NotificationLevel>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
