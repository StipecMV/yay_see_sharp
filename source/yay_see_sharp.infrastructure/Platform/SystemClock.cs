using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Platform;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateTimeOffset LocalNow => DateTimeOffset.Now;

    public TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;
}
