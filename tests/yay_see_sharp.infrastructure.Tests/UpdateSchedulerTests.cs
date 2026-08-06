using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Scheduling;

namespace yay_see_sharp.infrastructure.Tests;

public class UpdateSchedulerTests
{
    private static readonly TimeSpan TestPollInterval = TimeSpan.FromMilliseconds(20);

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }

        public DateTimeOffset LocalNow { get; set; }

        /// <summary>Defaults to UTC so every existing test that doesn't care about DST (they only assert Hour, not Offset) keeps working without having to set this explicitly.</summary>
        public TimeZoneInfo LocalTimeZone { get; set; } = TimeZoneInfo.Utc;
    }

    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

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

    /// <summary>
    /// Runs the scheduler against a fake clock fixed at "now" in the given zone, with the schedule
    /// time already behind "now" so the next run lands the following day — then jumps the fake
    /// clock there and asserts the check fires exactly at that local time, not the UTC equivalent.
    /// </summary>
    private static async Task AssertScheduledRunFiresAtLocalTime(TimeZoneInfo zone, DateTimeOffset now, TimeSpan expectedOffset)
    {
        var clock = new FakeClock { LocalNow = now, UtcNow = now.ToUniversalTime(), LocalTimeZone = zone };
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

            var nextRun = scheduler.NextScheduledRun!.Value;
            await Assert.That(nextRun.Offset).IsEqualTo(expectedOffset);
            await Assert.That(nextRun.Hour).IsEqualTo(8);

            clock.LocalNow = nextRun.AddSeconds(1);
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
    public async Task Scheduled_run_fires_at_the_configured_time_in_utc()
    {
        await AssertScheduledRunFiresAtLocalTime(
            TimeZoneInfo.Utc, new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero), TimeSpan.Zero);
    }

    [Test]
    public async Task Scheduled_run_fires_at_the_configured_local_time_in_bratislava_winter_offset()
    {
        // CET (winter): UTC+1. January is safely outside any DST transition window.
        await AssertScheduledRunFiresAtLocalTime(
            Bratislava, new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(1)), TimeSpan.FromHours(1));
    }

    [Test]
    public async Task Scheduled_run_fires_at_the_configured_local_time_in_bratislava_summer_offset()
    {
        // CEST (summer/DST): UTC+2 — a scheduler comparing against UTC instead of local time
        // would fire this two hours late. July is safely outside any DST transition window.
        await AssertScheduledRunFiresAtLocalTime(
            Bratislava, new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.FromHours(2)), TimeSpan.FromHours(2));
    }

    [Test]
    public async Task Disabled_scheduler_never_checks_for_updates_or_reports_a_next_run()
    {
        var clock = new FakeClock { LocalNow = DateTimeOffset.Now };
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
        var clock = new FakeClock { LocalNow = DateTimeOffset.Now };
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
    public async Task Changing_the_schedule_time_mid_run_invalidates_the_cached_next_run()
    {
        var clock = new FakeClock { LocalNow = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero) };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = true, UpdateScheduleTime = new TimeOnly(20, 0) };
        var backend = CreateBackend([]);
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);

        try
        {
            await scheduler.StartAsync();
            await WaitUntilAsync(() => scheduler.NextScheduledRun is not null, TimeSpan.FromSeconds(2));
            await Assert.That(scheduler.NextScheduledRun!.Value.Hour).IsEqualTo(20);

            settings.UpdateScheduleTime = new TimeOnly(21, 30);

            await WaitUntilAsync(
                () => scheduler.NextScheduledRun is { } next && next.Hour == 21 && next.Minute == 30,
                TimeSpan.FromSeconds(2));
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Test]
    public async Task Disabling_then_re_enabling_recomputes_the_next_run_from_scratch()
    {
        var clock = new FakeClock { LocalNow = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero) };
        var settings = new FakeSettings { AutoUpdateCheckEnabled = true, UpdateScheduleTime = new TimeOnly(20, 0) };
        var backend = CreateBackend([]);
        var scheduler = new UpdateScheduler(clock, backend.Object, settings, TestPollInterval);

        try
        {
            await scheduler.StartAsync();
            await WaitUntilAsync(() => scheduler.NextScheduledRun is not null, TimeSpan.FromSeconds(2));

            settings.AutoUpdateCheckEnabled = false;
            await WaitUntilAsync(() => scheduler.NextScheduledRun is null, TimeSpan.FromSeconds(2));

            settings.AutoUpdateCheckEnabled = true;
            await WaitUntilAsync(() => scheduler.NextScheduledRun is not null, TimeSpan.FromSeconds(2));

            await Assert.That(scheduler.NextScheduledRun!.Value.Hour).IsEqualTo(20);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    [Test]
    public async Task Concurrent_checks_are_serialized_so_a_second_trigger_is_skipped_not_queued()
    {
        var clock = new FakeClock { LocalNow = DateTimeOffset.Now };
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
