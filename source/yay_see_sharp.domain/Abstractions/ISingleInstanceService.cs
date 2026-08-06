namespace yay_see_sharp.domain.Abstractions;

public interface ISingleInstanceService : IDisposable
{
    /// <summary>Attempts to become the one running instance. Returns false when another instance already holds it — the caller should then use <see cref="TryActivateExisting"/> and exit.</summary>
    bool TryAcquire();

    /// <summary>Called by a second process that lost <see cref="TryAcquire"/>: asks the existing instance to raise <see cref="ActivationRequested"/> (typically to restore/focus its window). Returns false if no instance could be reached.</summary>
    bool TryActivateExisting();

    /// <summary>Raised on the instance that holds the lock when a later launch calls <see cref="TryActivateExisting"/>. Handlers run off the UI thread and must dispatch accordingly.</summary>
    event EventHandler? ActivationRequested;
}
