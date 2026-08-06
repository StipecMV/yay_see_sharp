namespace yay_see_sharp.domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current time in the host's local timezone. Scheduling (e.g. the daily update-check time) is user-facing and must be interpreted against this, not <see cref="UtcNow"/>.</summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>The zone <see cref="LocalNow"/> is expressed in. Needed separately from <see cref="LocalNow"/> because scheduling a *future* occurrence (e.g. "tomorrow at 10:00") requires the zone's actual transition rules, not just the UTC offset "now" happens to carry — that offset can be wrong for a date on the other side of a DST transition.</summary>
    TimeZoneInfo LocalTimeZone { get; }
}
