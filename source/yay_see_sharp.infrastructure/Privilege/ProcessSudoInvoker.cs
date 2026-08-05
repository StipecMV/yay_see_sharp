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
    public async Task<bool> ValidateTimestampAsync(CancellationToken cancellationToken)
    {
        using var process = Start(["-n", "-v"], redirectStandardInput: false);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
    }

    public async Task<bool> RefreshWithPasswordAsync(string password, CancellationToken cancellationToken)
    {
        using var process = Start(["-S", "-v"], redirectStandardInput: true);
        try
        {
            await process.StandardInput.WriteLineAsync(password);
        }
        finally
        {
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0;
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
