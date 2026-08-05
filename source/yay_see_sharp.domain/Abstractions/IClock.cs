namespace yay_see_sharp.domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
