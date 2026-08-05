using System;
using System.Threading.Tasks;
using yay_see_sharp.domain.Scheduling;

public class UpdateScheduleCalculatorTests
{
    [Test]
    public async Task Next_run_is_later_today_when_scheduled_time_has_not_passed()
    {
        var now = new DateTimeOffset(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Next_run_rolls_over_to_tomorrow_when_scheduled_time_has_passed()
    {
        var now = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        var scheduledTime = new TimeOnly(10, 0);

        var next = UpdateScheduleCalculator.GetNextRun(now, scheduledTime);

        await Assert.That(next).IsEqualTo(new DateTimeOffset(2026, 8, 2, 10, 0, 0, TimeSpan.Zero));
    }
}
