namespace yay_see_sharp.domain.Abstractions;

public interface ISingleInstanceService : IDisposable
{
    /// <summary>
    /// Attempts to become the one running instance. Returns false when this process should not
    /// run — either because another instance already holds it (the common, expected case; see
    /// <see cref="LastFailureReason"/> to tell them apart) or because becoming the primary instance
    /// failed partway through. This call is transactional: a false return never leaves a lock held
    /// without a functional activation listener behind it — on any failure after the lock itself
    /// was acquired, the lock is released before returning. The caller should then use
    /// <see cref="TryActivateExisting"/> and exit.
    /// </summary>
    bool TryAcquire();

    /// <summary>Called by a second process that lost <see cref="TryAcquire"/>: asks the existing instance to raise <see cref="ActivationRequested"/> (typically to restore/focus its window). Returns false if no instance could be reached.</summary>
    bool TryActivateExisting();

    /// <summary>Raised on the instance that holds the lock when a later launch calls <see cref="TryActivateExisting"/>. Handlers run off the UI thread and must dispatch accordingly.</summary>
    event EventHandler? ActivationRequested;

    /// <summary>
    /// Set after a <see cref="TryAcquire"/> that returned false, explaining why: null for the
    /// ordinary case (another instance already holds the lock), or a human-readable message for a
    /// distinct problem — the runtime directory has unsafe/foreign-owned permissions, or the IPC
    /// activation listener itself failed to start — so a caller that wants to surface something
    /// more specific than "another copy is already running" can.
    /// </summary>
    string? LastFailureReason { get; }
}
