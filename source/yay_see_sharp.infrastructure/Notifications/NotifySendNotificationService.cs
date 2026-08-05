using Splat;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Process;

namespace yay_see_sharp.infrastructure.Notifications;

/// <summary>
/// Sends desktop notifications via libnotify's `notify-send` CLI. Shelling out is fine here —
/// unlike sudo, this never carries user-entered credentials or arbitrary user input through a
/// shell, only fixed flags plus the title/body strings the app itself constructs.
/// </summary>
public sealed class NotifySendNotificationService : INotificationService, IEnableLogger
{
    private const string AppName = "Yay See Sharp";

    private readonly ICommandRunner _commandRunner;

    public NotifySendNotificationService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task SendAsync(
        string title,
        string body,
        NotificationLevel level = NotificationLevel.Info,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var urgency = level switch
            {
                NotificationLevel.Error => "critical",
                NotificationLevel.Warning => "normal",
                _ => "low",
            };

            var result = await _commandRunner.RunAsync(
                new CommandRequest("notify-send", [$"--app-name={AppName}", $"--urgency={urgency}", title, body]),
                cancellationToken: cancellationToken);

            if (!result.Succeeded)
            {
                this.Log().Warn($"notify-send exited with code {result.ExitCode}; desktop notification was not shown.");
            }
        }
        catch (Exception ex)
        {
            // Best-effort: notify-send/libnotify might not be installed (minimal WM, headless,
            // sandboxed session, ...). A failed notification must never interrupt or fail the
            // package operation that triggered it.
            this.Log().Warn(ex, "Failed to send a desktop notification; continuing without it.");
        }
    }
}
