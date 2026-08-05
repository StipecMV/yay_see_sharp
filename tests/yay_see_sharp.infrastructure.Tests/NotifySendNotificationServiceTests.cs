using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Notifications;
using yay_see_sharp.infrastructure.Process;

public class NotifySendNotificationServiceTests
{
    [Test]
    public async Task Send_invokes_notify_send_with_the_title_and_body()
    {
        var runner = new Mock<ICommandRunner>();
        runner.Setup(r => r.RunAsync(
                It.Is<CommandRequest>(request => request.FileName == "notify-send" &&
                    request.Arguments.Contains("Updates available") &&
                    request.Arguments.Contains("3 packages can be updated")),
                It.IsAny<IProgress<CommandOutput>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));
        var service = new NotifySendNotificationService(runner.Object);

        await service.SendAsync("Updates available", "3 packages can be updated", NotificationLevel.Info);

        runner.Verify(r => r.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Send_maps_error_level_to_critical_urgency()
    {
        var runner = new Mock<ICommandRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(0, [], false));
        var service = new NotifySendNotificationService(runner.Object);

        await service.SendAsync("Install failed", "exit code 1", NotificationLevel.Error);

        runner.Verify(r => r.RunAsync(
            It.Is<CommandRequest>(request => request.Arguments.Contains("--urgency=critical")),
            It.IsAny<IProgress<CommandOutput>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Send_does_not_throw_when_the_command_runner_throws()
    {
        var runner = new Mock<ICommandRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("notify-send not found"));
        var service = new NotifySendNotificationService(runner.Object);

        await service.SendAsync("Title", "Body");

        // Reaching this point without an exception is the real assertion — a missing notify-send
        // must never surface as a failure to whatever triggered the notification.
        runner.Verify(r => r.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Send_does_not_throw_when_notify_send_exits_non_zero()
    {
        var runner = new Mock<ICommandRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommandResult(1, [], false));
        var service = new NotifySendNotificationService(runner.Object);

        await service.SendAsync("Title", "Body");

        runner.Verify(r => r.RunAsync(
            It.IsAny<CommandRequest>(), It.IsAny<IProgress<CommandOutput>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
