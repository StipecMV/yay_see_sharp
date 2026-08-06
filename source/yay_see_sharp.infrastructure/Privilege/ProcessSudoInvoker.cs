using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace yay_see_sharp.infrastructure.Privilege;

/// <summary>
/// Real `sudo` process invocations. The password only ever travels over the child process's
/// stdin pipe — it is never placed in an argument list (which would be visible to every other
/// process on the machine via /proc/&lt;pid&gt;/cmdline) and no output is logged anywhere.
/// </summary>
public sealed class ProcessSudoInvoker : ISudoInvoker
{
    /// <summary>`sudo -n -v` should return near-instantly (it never prompts); a hang here almost certainly means a stuck PAM module, not a legitimate slow check.</summary>
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(10);

    public async Task<bool> ValidateTimestampAsync(CancellationToken cancellationToken)
    {
        using var process = Start(["-n", "-v"], redirectStandardInput: false);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ValidationTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Covers both a caller-requested cancellation and our own timeout firing — either
            // way the check didn't complete, so terminate the child rather than leave it running,
            // and report "not validated" instead of letting the exception escape.
            await TerminateAsync(process);
            return false;
        }

        return process.ExitCode == 0;
    }

    public async Task<bool> RefreshWithPasswordAsync(string password, CancellationToken cancellationToken)
    {
        using var process = Start(["-S", "-v"], redirectStandardInput: true);
        try
        {
            try
            {
                await process.StandardInput.WriteLineAsync(password);
            }
            finally
            {
                // Close immediately after writing — the password must not linger reachable in an
                // open pipe for any longer than it takes sudo to read the one line it needs.
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            await TerminateAsync(process);
            return false;
        }
    }

    /// <summary>Kills the child (if still running) and awaits its exit on an uncancellable token, so cleanup itself can never throw a second OperationCanceledException.</summary>
    private static async Task TerminateAsync(DiagnosticsProcess process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private static DiagnosticsProcess Start(IReadOnlyList<string> arguments, bool redirectStandardInput)
    {
        var startInfo = new DiagnosticsProcessStartInfo
        {
            FileName = "sudo",
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new DiagnosticsProcess { StartInfo = startInfo };
        process.Start();
        return process;
    }
}
