using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Scheduling;

public class UpdateSchedulerTests
{
    private static readonly TimeSpan TestPollInterval = TimeSpan.FromMilliseconds(20);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class FakeSettings : IUpdateScheduleSettings
    {
        public bool AutoUpdateCheckEnabled { get; set; }

        public TimeOnly UpdateScheduleTime { get; set; }
    }

    private static Mock<IPackageBackend> CreateBackend(IReadOnlyList<UpdateInfo> updates)
    {
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(updates);
        return backend;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within the timeout.");
        }
    }

    [Test]
    public async Task Scheduled_run_fires_once_the_clock_reaches_the_next_scheduled_time()
    {
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero) };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = true, UpdateScheduleTime = new TimeOnly(8, 0) };
        var updates = new[] { new UpdateInfo("firefox", "1.0", "2.0", PackageSource.Official, 0) };
        var backend = CreateBackend(updates);
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);
        IReadOnlyList<UpdateInfo>? received = null;
        scheduler.OnUpdatesFound = found =>
        {
            received = found;
            return Task.CompletedTask;
        };

        try
        {
            await scheduler.StartAsync();
            await WaitUntilAsync(() => scheduler.NextScheduledRun is not null, TimeSpan.FromSeconds(2));

            // The schedule time (08:00) is already behind "now" (09:00), so the next run lands
            // tomorrow at 08:00 — jump the fake clock there instead of waiting a real day.
            clock.UtcNow = scheduler.NextScheduledRun!.Value.AddSeconds(1);

            await WaitUntilAsync(() => received is not null, TimeSpan.FromSeconds(2));
        }
        finally
        {
            await scheduler.StopAsync();
        }

        await Assert.That(received).IsNotNull();
        await Assert.That(received!.Count).IsEqualTo(1);
        await Assert.That(received[0].Name).IsEqualTo("firefox");
    }

    [Test]
    public async Task Disabled_scheduler_never_checks_for_updates_or_reports_a_next_run()
    {
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = false, UpdateScheduleTime = new TimeOnly(0, 0) };
        var backend = CreateBackend([]);
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);

        try
        {
            await scheduler.StartAsync();
            await Task.Delay(TestPollInterval * 5);
        }
        finally
        {
            await scheduler.StopAsync();
        }

        await Assert.That(scheduler.NextScheduledRun).IsNull();
        backend.Verify(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Stop_cancels_the_background_loop_and_clears_the_next_scheduled_run()
    {
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = true, UpdateScheduleTime = new TimeOnly(0, 0) };
        var backend = CreateBackend([]);
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);
        await scheduler.StartAsync();
        await WaitUntilAsync(() => scheduler.NextScheduledRun is not null, TimeSpan.FromSeconds(2));

        await scheduler.StopAsync();

        await Assert.That(scheduler.NextScheduledRun).IsNull();

        // Stopping must be safe to call again and safe to await without hanging.
        await scheduler.StopAsync();
    }

    [Test]
    public async Task Concurrent_checks_are_serialized_so_a_second_trigger_is_skipped_not_queued()
    {
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = false, UpdateScheduleTime = new TimeOnly(0, 0) };
        var gate = new TaskCompletionSource();
        var backend = new Mock<IPackageBackend>();
        var callCount = 0;
        backend.Setup(b => b.GetUpdatesAsync(It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref callCount);
                await gate.Task;
                return (IReadOnlyList<UpdateInfo>)[];
            });
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);

        var firstCall = scheduler.TryRunCheckNowAsync();
        await Task.Delay(50); // let the first call actually enter the lock before the second races in
        var secondCall = scheduler.TryRunCheckNowAsync();
        var secondResult = await secondCall;
        gate.SetResult();
        var firstResult = await firstCall;

        await Assert.That(firstResult).IsTrue();
        await Assert.That(secondResult).IsFalse();
        await Assert.That(callCount).IsEqualTo(1);
    }
}
