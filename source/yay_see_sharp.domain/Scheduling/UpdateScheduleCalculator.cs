namespace yay_see_sharp.domain.Scheduling;

public static class UpdateScheduleCalculator
{
    public static DateTimeOffset GetNextRun(DateTimeOffset now, TimeOnly scheduledTime)
    {
        var candidate = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            scheduledTime.Hour, scheduledTime.Minute, scheduledTime.Second,
            now.Offset);

        return candidate > now ? candidate : candidate.AddDays(1);
    }
}
