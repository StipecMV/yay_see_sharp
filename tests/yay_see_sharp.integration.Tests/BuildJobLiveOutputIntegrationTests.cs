using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class BuildJobLiveOutputIntegrationTests
{
    /// <summary>
    /// Reports synchronously on the calling thread. <see cref="Progress{T}"/> always marshals
    /// through the captured SynchronizationContext via Post, which is asynchronous even with the
    /// default context — that let a queued "Applying" update race past a later, synchronously
    /// applied terminal stage and made IsRunning flicker back to true. This type removes that race.
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Test]
    public async Task Running_a_real_shell_command_streams_both_output_lines_into_the_build_job_log()
    {
        if (!IntegrationSkip.IsOnPath("bash"))
        {
            throw new TUnit.Core.Exceptions.SkipTestException("bash was not found on PATH.");
        }

        var localization = new LocalizationService("en");
        var operation = new OperationViewModel(PackageOperationKind.Install, localization);
        var buildJob = new BuildJobViewModel("integration-echo-test", operation, localization);
        var runner = new SystemCommandRunner();

        var progress = new SynchronousProgress<CommandOutput>(output =>
        {
            operation.Apply(new PackageOperationProgress(
                PackageOperationKind.Install, PackageOperationStage.Applying, 50, output.Text, null, output.Text));
        });

        var request = new CommandRequest("bash", ["-c", "echo hello && sleep 0.1 && echo world"]);
        var result = await runner.RunAsync(request, progress);

        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Completed, 100, "done"));

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(buildJob.LogLines.Any(line => line.Contains("hello"))).IsTrue();
        await Assert.That(buildJob.LogLines.Any(line => line.Contains("world"))).IsTrue();
        await Assert.That(buildJob.Operation.IsRunning).IsFalse();
    }

    [Test]
    public async Task A_failing_command_is_reflected_in_the_process_exit_code_and_still_reaches_a_terminal_stage()
    {
        if (!IntegrationSkip.IsOnPath("bash"))
        {
            throw new TUnit.Core.Exceptions.SkipTestException("bash was not found on PATH.");
        }

        var localization = new LocalizationService("en");
        var operation = new OperationViewModel(PackageOperationKind.Install, localization);
        var buildJob = new BuildJobViewModel("integration-failure-test", operation, localization);
        var runner = new SystemCommandRunner();

        var progress = new SynchronousProgress<CommandOutput>(output =>
        {
            operation.Apply(new PackageOperationProgress(
                PackageOperationKind.Install, PackageOperationStage.Applying, 50, output.Text, null, output.Text));
        });

        var request = new CommandRequest("bash", ["-c", "echo about-to-fail && exit 7"]);
        var result = await runner.RunAsync(request, progress);

        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Failed, 100, "failed"));

        await Assert.That(result.ExitCode).IsEqualTo(7);
        await Assert.That(buildJob.LogLines.Any(line => line.Contains("about-to-fail"))).IsTrue();
        await Assert.That(buildJob.Operation.IsRunning).IsFalse();
    }
}
