using System.Linq;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Process;

public class CommandRunnerTests
{
    [Test]
    public async Task System_command_runner_captures_standard_output_and_exit_code()
    {
        var runner = new SystemCommandRunner();
        var result = await runner.RunAsync(new CommandRequest("sh", ["-c", "printf hello"]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.CombinedText).Contains("hello");
    }

    [Test]
    public async Task System_command_runner_reports_standard_error_and_failure()
    {
        var runner = new SystemCommandRunner();
        var result = await runner.RunAsync(new CommandRequest("sh", ["-c", "printf problem >&2; exit 7"]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(7);
        await Assert.That(result.Output.Any(line => line.Kind == CommandOutputKind.StandardError)).IsTrue();
        await Assert.That(result.CombinedText).Contains("problem");
    }
}
