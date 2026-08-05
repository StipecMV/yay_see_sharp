namespace yay_see_sharp.domain.Abstractions;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
}
