using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class BackendInstallPromptViewModelTests
{
    private sealed class FakeBackendInstaller : IBackendInstaller
    {
        public required Func<CancellationToken, IAsyncEnumerable<PackageOperationProgress>> Behavior { get; init; }

        public int CallCount { get; private set; }

        public string DisplayCommand => "fake install command";

        public IAsyncEnumerable<PackageOperationProgress> InstallAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Behavior(cancellationToken);
        }
    }

    private static async IAsyncEnumerable<PackageOperationProgress> SucceedsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Preparing, 5, "Preparing");
        await Task.Yield();
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Completed, 100, "Done");
    }

    private static async IAsyncEnumerable<PackageOperationProgress> FailsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Preparing, 5, "Preparing");
        await Task.Yield();
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Failed, 0, "boom");
    }

    private static async IAsyncEnumerable<PackageOperationProgress> ThrowsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Preparing, 5, "Preparing");
        await Task.Yield();
        throw new InvalidOperationException("disk full");
    }

    private static async IAsyncEnumerable<PackageOperationProgress> HangsUntilCancelledAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PackageOperationProgress(PackageOperationKind.InstallBackend, PackageOperationStage.Downloading, 30, "Working");
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within the timeout.");
        }
    }

    [Test]
    public async Task Successful_install_reaches_completed_and_shows_the_restart_hint()
    {
        var installer = new FakeBackendInstaller { Behavior = SucceedsAsync };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        await viewModel.ConfirmCommand.Execute();

        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(viewModel.ShowRestartHint).IsTrue();
    }

    [Test]
    public async Task Failed_install_leaves_the_modal_closable_and_the_operation_repeatable()
    {
        var installer = new FakeBackendInstaller { Behavior = FailsAsync };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        await viewModel.ConfirmCommand.Execute();

        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(viewModel.Operation!.IsRunning).IsFalse();
        await Assert.That(await viewModel.CloseCommand.CanExecute.FirstAsync()).IsTrue();
        await Assert.That(await viewModel.ConfirmCommand.CanExecute.FirstAsync()).IsTrue();
    }

    [Test]
    public async Task An_installer_that_throws_still_ends_in_a_terminal_closable_retryable_state()
    {
        var installer = new FakeBackendInstaller { Behavior = ThrowsAsync };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        await viewModel.ConfirmCommand.Execute();

        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Failed);
        await Assert.That(viewModel.Operation!.Message).Contains("disk full");
        await Assert.That(viewModel.Operation!.IsRunning).IsFalse();
        await Assert.That(await viewModel.CloseCommand.CanExecute.FirstAsync()).IsTrue();
        await Assert.That(await viewModel.ConfirmCommand.CanExecute.FirstAsync()).IsTrue();
    }

    [Test]
    public async Task Retrying_after_a_failure_invokes_the_installer_again_and_can_succeed()
    {
        var firstAttempt = true;
        var installer = new FakeBackendInstaller
        {
            Behavior = ct =>
            {
                if (firstAttempt)
                {
                    firstAttempt = false;
                    return FailsAsync(ct);
                }

                return SucceedsAsync(ct);
            },
        };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        await viewModel.ConfirmCommand.Execute();
        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Failed);

        await viewModel.ConfirmCommand.Execute();

        await Assert.That(installer.CallCount).IsEqualTo(2);
        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Completed);
    }

    [Test]
    public async Task Confirm_is_disabled_while_an_attempt_is_still_running()
    {
        var installer = new FakeBackendInstaller { Behavior = HangsUntilCancelledAsync };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        viewModel.ConfirmCommand.Execute().Subscribe();
        await WaitUntilAsync(() => viewModel.Operation is { IsRunning: true }, TimeSpan.FromSeconds(2));

        await Assert.That(await viewModel.ConfirmCommand.CanExecute.FirstAsync()).IsFalse();
        await Assert.That(await viewModel.CloseCommand.CanExecute.FirstAsync()).IsFalse();

        viewModel.Operation!.CancelCommand.Execute().Subscribe();
        await WaitUntilAsync(() => viewModel.Operation is { IsRunning: false }, TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task Cancel_command_on_the_operation_cancels_the_in_flight_installer_and_reports_cancelled()
    {
        var installer = new FakeBackendInstaller { Behavior = HangsUntilCancelledAsync };
        var viewModel = new BackendInstallPromptViewModel(installer, new LocalizationService("en"));

        viewModel.ConfirmCommand.Execute().Subscribe();
        await WaitUntilAsync(() => viewModel.Operation is { IsRunning: true }, TimeSpan.FromSeconds(2));

        await viewModel.Operation!.CancelCommand.Execute();
        await WaitUntilAsync(() => viewModel.Operation is { IsRunning: false }, TimeSpan.FromSeconds(2));

        await Assert.That(viewModel.Operation!.Stage).IsEqualTo(PackageOperationStage.Cancelled);
    }
}
