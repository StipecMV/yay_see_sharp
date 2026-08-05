namespace yay_see_sharp.domain.Abstractions;

/// <summary>Background service that checks for package updates once a day at the user's configured time.</summary>
public interface IUpdateScheduler
{
    DateTimeOffset? NextScheduledRun { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync();
}
