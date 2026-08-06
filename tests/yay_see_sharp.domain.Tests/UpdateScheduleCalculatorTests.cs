using System;
using System.Threading.Tasks;
using yay_see_sharp.domain.Scheduling;

namespace yay_see_sharp.domain.Tests;

public class UpdateScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Bratislava = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bratislava");

    [Test]
    public async Task Next_run_is_later_today_when_scheduled_time_has_not_passed()
    {
        var now = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, TimeZoneInfo.Utc);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Next_run_rolls_over_to_tomorrow_when_scheduled_time_has_passed()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, TimeZoneInfo.Utc);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Next_run_resolves_correctly_in_bratislava_winter_offset_cet()
    {
        // 2026-01-15 is deep winter — Bratislava is CET (UTC+1), no DST in effect.
        var now = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(1));
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, Bratislava);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.FromHours(1)));
        await Assert.That(next.Offset).IsEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task Next_run_resolves_correctly_in_bratislava_summer_offset_cest()
    {
        // 2026-07-15 is deep summer — Bratislava is CEST (UTC+2), DST in effect.
        var now = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.FromHours(2));
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, Bratislava);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.FromHours(2)));
        await Assert.That(next.Offset).IsEqualTo(TimeSpan.FromHours(2));
    }

    [Test]
    public async Task Next_run_crosses_the_spring_forward_transition_and_lands_on_the_correct_offset()
    {
        // Bratislava jumps 02:00 CET -> 03:00 CEST on 2026-03-29. Scheduled for 10:00, computed
        // from the evening before (still CET) — the result must report the *new* CEST offset for
        // the target day, not the CET offset "now" happened to carry.
        var now = new DateTimeOffset(2026, 3, 28, 20, 0, 0, TimeSpan.FromHours(1));
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, Bratislava);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 3, 29, 10, 0, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task Next_run_rolls_a_nonexistent_spring_forward_scheduled_time_forward_to_the_first_valid_instant()
    {
        // 02:00-02:59:59 doesn't exist on 2026-03-29 in Bratislava (clocks jump straight to 03:00
        // CEST). A schedule of 02:30 must resolve to the first instant that actually exists: 03:00.
        var now = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));
        var scheduledTime = new TimeOnly(2, 30);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, Bratislava);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task Next_run_resolves_an_ambiguous_fall_back_scheduled_time_to_the_standard_time_occurrence()
    {
        // 02:00-02:59:59 occurs twice on 2026-10-25 in Bratislava (clocks fall back from 03:00
        // CEST to 02:00 CET). A schedule of 02:30 is ambiguous; the standard-time (CET, second,
        // later) occurrence is the documented default for TimeZoneInfo.ConvertTimeToUtc.
        var now = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(2));
        var scheduledTime = new TimeOnly(2, 30);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime, Bratislava);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1)));
    }
}
