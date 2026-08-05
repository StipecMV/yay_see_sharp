using DiagnosticsProcess = System.Diagnostics.Process;
using DiagnosticsProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace yay_see_sharp.infrastructure.Process;

public sealed class SystemCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(
        CommandRequest request,
        IProgress<CommandOutput>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("A command filename is required.", nameof(request));
        }

        var startInfo = new DiagnosticsProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : request.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new DiagnosticsProcess { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new List<CommandOutput>();
        var outputLock = new object();

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start command '{request.FileName}'.");
        }

        async Task ReadStreamAsync(StreamReader reader, CommandOutputKind kind)
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var item = new CommandOutput(kind, line, DateTimeOffset.UtcNow);
                lock (outputLock)
                {
                    output.Add(item);
                }
                progress?.Report(item);
            }
        }

        var stdoutTask = ReadStreamAsync(process.StandardOutput, CommandOutputKind.StandardOutput);
        var stderrTask = ReadStreamAsync(process.StandardError, CommandOutputKind.StandardError);
        var wasCancelled = false;

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            TryTerminate(process);
            await Task.WhenAll(
                IgnoreCancellationAsync(stdoutTask),
                IgnoreCancellationAsync(stderrTask));
        }

        return new CommandResult(process.HasExited ? process.ExitCode : -1, output, wasCancelled);
    }

    private static void TryTerminate(DiagnosticsProcess process)
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
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
