namespace yay_see_sharp.infrastructure.Process;

public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public enum CommandOutputKind
{
    StandardOutput,
    StandardError,
}

public sealed record CommandOutput(
    CommandOutputKind Kind,
    string Text,
    DateTimeOffset Timestamp);

public sealed record CommandResult(
    int ExitCode,
    IReadOnlyList<CommandOutput> Output,
    bool WasCancelled)
{
    public bool Succeeded => !WasCancelled && ExitCode == 0;

    public string CombinedText => string.Join(
        Environment.NewLine,
        Output.Select(line => line.Text));
}
