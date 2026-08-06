namespace yay_see_sharp.domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current time in the host's local timezone. Scheduling (e.g. the daily update-check time) is user-facing and must be interpreted against this, not <see cref="UtcNow"/>.</summary>
    DateTimeOffset LocalNow { get; }
}
