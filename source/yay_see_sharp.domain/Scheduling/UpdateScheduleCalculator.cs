namespace yay_see_sharp.domain.Scheduling;

public static class UpdateScheduleCalculator
{
    /// <summary>Convenience overload for callers that don't need to inject a specific zone (e.g. quick scripts, ad-hoc tools). Production scheduling code should call the <see cref="TimeZoneInfo"/> overload explicitly with the clock's zone, so tests can exercise DST transitions deterministically instead of depending on the host machine's zone.</summary>
    public static DateTimeOffset GetNextRun(DateTimeOffset now, TimeOnly scheduledTime) =>
        GetNextRun(now, scheduledTime, TimeZoneInfo.Local);

    /// <summary>
    /// Computes the next occurrence of <paramref name="scheduledTime"/> strictly after
    /// <paramref name="now"/>, resolved against <paramref name="timeZone"/>'s actual transition
    /// rules — not the fixed UTC offset <paramref name="now"/> happens to carry. A schedule
    /// computed the day before a DST transition must still land on the correct wall-clock instant
    /// on the transition day itself, which requires knowing the zone's rules, not just today's
    /// offset.
    ///
    /// Spring-forward (the scheduled wall-clock time falls in the nonexistent gap — e.g. 02:30 on
    /// the day a zone jumps 02:00 → 03:00): rolled forward minute-by-minute to the first valid
    /// instant at or after the gap (03:00 in that example). The scheduled check still runs on the
    /// same calendar day, just at the earliest wall-clock moment that actually exists.
    ///
    /// Fall-back (the scheduled wall-clock time is ambiguous — e.g. 02:30 occurs twice when a zone
    /// falls back 03:00 → 02:00): resolved to the second, standard-time occurrence, matching
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>'s documented default
    /// for ambiguous unspecified-kind times (it assumes the time is not daylight saving time).
    /// </summary>
    public static DateTimeOffset GetNextRun(DateTimeOffset now, TimeOnly scheduledTime, TimeZoneInfo timeZone)
    {
        var localDate = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        var candidate = BuildInstant(localDate, scheduledTime, timeZone);

        return candidate > now ? candidate : BuildInstant(localDate.AddDays(1), scheduledTime, timeZone);
    }

    private static DateTimeOffset BuildInstant(DateTime date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var wallClock = new DateTime(
            date.Year, date.Month, date.Day,
            time.Hour, time.Minute, time.Second,
            DateTimeKind.Unspecified);

        // Spring-forward gaps are a single DST delta wide (virtually always 1 hour) — bounding the
        // walk at a full day is generous headroom without risking an infinite loop on malformed
        // zone data.
        var minutesRolled = 0;
        while (timeZone.IsInvalidTime(wallClock) && minutesRolled < 24 * 60)
        {
            wallClock = wallClock.AddMinutes(1);
            minutesRolled++;
        }

        var utc = TimeZoneInfo.ConvertTimeToUtc(wallClock, timeZone);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero), timeZone);
    }
}
