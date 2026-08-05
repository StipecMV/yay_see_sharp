namespace yay_see_sharp.infrastructure.Process;

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(
        CommandRequest request,
        IProgress<CommandOutput>? progress = null,
        CancellationToken cancellationToken = default);
}
