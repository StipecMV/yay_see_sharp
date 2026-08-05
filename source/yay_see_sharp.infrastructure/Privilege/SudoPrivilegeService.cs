using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Privilege;

/// <summary>
/// IPrivilegeService backed by the real `sudo` timestamp cache. The password-prompt callback is
/// a settable property rather than a constructor argument: this service is constructed before
/// MainWindowViewModel exists (the package backend needs it earlier than the window does), so the
/// UI-facing prompt is wired in once the window view model is available. Until then, a missing
/// prompt just means elevation requests fail closed rather than throwing.
///
/// Security: the password is only ever read into a local `string` for the duration of one sudo
/// call and never stored on this type, logged, or placed in a command-line argument list. Note
/// that .NET strings are immutable, so dropping the reference (rather than true in-place
/// scrubbing, which would need char[]/SecureString) is the realistic mitigation here — this
/// matches current .NET guidance, which no longer recommends SecureString for cross-platform code.
/// </summary>
public sealed class SudoPrivilegeService : IPrivilegeService
{
    private readonly ISudoInvoker _invoker;
    private bool _isElevated;

    public SudoPrivilegeService(ISudoInvoker? invoker = null)
    {
        _invoker = invoker ?? new ProcessSudoInvoker();
    }

    public Func<CancellationToken, Task<string?>>? PasswordPrompt { get; set; }

    public bool IsElevated => _isElevated;

    public async Task<PrivilegeResult> RequestElevationAsync(CancellationToken cancellationToken = default)
    {
        if (await _invoker.ValidateTimestampAsync(cancellationToken))
        {
            _isElevated = true;
            return PrivilegeResult.Granted;
        }

        _isElevated = false;

        if (PasswordPrompt is null)
        {
            return PrivilegeResult.Failed;
        }

        var password = await PasswordPrompt(cancellationToken);
        if (string.IsNullOrEmpty(password))
        {
            return PrivilegeResult.Cancelled;
        }

        try
        {
            var succeeded = await _invoker.RefreshWithPasswordAsync(password, cancellationToken);
            _isElevated = succeeded;
            return succeeded ? PrivilegeResult.Granted : PrivilegeResult.Failed;
        }
        finally
        {
            password = null;
        }
    }

    public async Task<PrivilegeResult> RefreshIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (!_isElevated)
        {
            return PrivilegeResult.Cancelled;
        }

        var stillValid = await _invoker.ValidateTimestampAsync(cancellationToken);
        _isElevated = stillValid;
        return stillValid ? PrivilegeResult.Granted : PrivilegeResult.Failed;
    }
}
