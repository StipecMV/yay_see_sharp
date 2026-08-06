using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.domain.Scheduling;

namespace yay_see_sharp.infrastructure.Scheduling;

/// <summary>
/// Polls at a short interval rather than sleeping until the next scheduled run: it keeps the
/// implementation trivially cancellable and lets it react immediately if the user changes the
/// auto-update toggle or schedule time mid-session, at the cost of a cheap once-a-minute wakeup.
/// Never runs on the UI thread — StartAsync only launches the loop via Task.Run and returns.
/// </summary>
public sealed class UpdateScheduler : IUpdateScheduler
{
    private readonly IClock _clock;
    private readonly IPackageBackend _backend;
    private readonly IUpdateScheduleSettings _settings;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private TimeOnly? _lastObservedScheduleTime;

    public UpdateScheduler(
        IClock clock,
        IPackageBackend backend,
        IUpdateScheduleSettings settings,
        TimeSpan? pollInterval = null)
    {
        _clock = clock;
        _backend = backend;
        _settings = settings;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>Invoked with the update list whenever a scheduled (or manually triggered) check finds any. Never invoked concurrently with itself.</summary>
    public Func<IReadOnlyList<UpdateInfo>, Task>? OnUpdatesFound { get; set; }

    public DateTimeOffset? NextScheduledRun { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var loopTask = _loopTask;
        if (cts is null)
        {
            return;
        }

        _cts = null;
        _loopTask = null;
        NextScheduledRun = null;

        await cts.CancelAsync();
        try
        {
            if (loopTask is not null)
            {
                await loopTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>Runs a check immediately, guarded by the same lock the scheduled loop uses. Returns false if a check was already in flight (the trigger is skipped, not queued).</summary>
    public async Task<bool> TryRunCheckNowAsync(CancellationToken cancellationToken = default)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            var updates = await _backend.GetUpdatesAsync(cancellationToken);
            if (updates.Count > 0 && OnUpdatesFound is { } handler)
            {
                await handler(updates);
            }

            return true;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_settings.AutoUpdateCheckEnabled)
                {
                    // The user picks the schedule time on their own clock, not UTC — comparing
                    // against LocalNow (rather than UtcNow) is what makes "10:00" actually mean
                    // 10:00 for the user, including across DST transitions (DateTimeOffset.Now's
                    // Offset always reflects the current local UTC offset).
                    if (_lastObservedScheduleTime != _settings.UpdateScheduleTime)
                    {
                        // A mid-session schedule change invalidates any cached target — otherwise
                        // the scheduler would keep waiting for the old time instead of the newly
                        // configured one.
                        NextScheduledRun = null;
                        _lastObservedScheduleTime = _settings.UpdateScheduleTime;
                    }

                    var now = _clock.LocalNow;

                    // GetNextRun always returns a time strictly after the "now" passed to it, so
                    // the target is computed once and cached — recomputing it every tick from the
                    // current "now" would make the due-check below never fire.
                    NextScheduledRun ??= UpdateScheduleCalculator.GetNextRun(now, _settings.UpdateScheduleTime, _clock.LocalTimeZone);

                    if (now >= NextScheduledRun.Value)
                    {
                        await TryRunCheckNowAsync(cancellationToken);
                        NextScheduledRun = UpdateScheduleCalculator.GetNextRun(_clock.LocalNow, _settings.UpdateScheduleTime, _clock.LocalTimeZone);
                    }
                }
                else
                {
                    // Disabling (or never having enabled) auto-update-check clears both the
                    // cached target and the last-observed time, so re-enabling always starts from
                    // a clean recompute rather than a stale target from before it was turned off.
                    NextScheduledRun = null;
                    _lastObservedScheduleTime = null;
                }

                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
