using System.ComponentModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

public class BuildJobViewModelTests
{
    [Test]
    public async Task Log_lines_reflect_output_appended_to_the_wrapped_operation()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("spotify-launcher", operation, new LocalizationService("en"));

        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Preparing, 10, "Preparing", "yay -S spotify-launcher", "==> Making package"));
        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Downloading, 40, "Downloading", null, "-> Downloading source"));

        await Assert.That(viewModel.LogLines.Count).IsEqualTo(2);
        await Assert.That(viewModel.LogLines[0]).IsEqualTo("==> Making package");
        await Assert.That(viewModel.LogLines[1]).IsEqualTo("-> Downloading source");
    }

    [Test]
    public async Task Log_lines_property_change_notification_fires_when_output_grows()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("hello", operation, new LocalizationService("en"));

        var raised = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BuildJobViewModel.LogLines))
            {
                raised = true;
            }
        };

        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Preparing, 5, "Preparing", null, "==> Starting"));

        await Assert.That(raised).IsTrue();
    }

    [Test]
    public async Task Progress_and_stage_updates_on_the_operation_are_visible_through_the_build_job()
    {
        var operation = new OperationViewModel(PackageOperationKind.Update, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("firefox", operation, new LocalizationService("en"));

        operation.Apply(new PackageOperationProgress(PackageOperationKind.Update, PackageOperationStage.Downloading, 63, "Downloading"));

        await Assert.That(viewModel.Operation.Percent).IsEqualTo(63);
        await Assert.That(viewModel.Operation.Stage).IsEqualTo(PackageOperationStage.Downloading);
    }

    [Test]
    public async Task Minimize_command_sets_is_minimized_so_the_job_keeps_running_in_the_background()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("discord", operation, new LocalizationService("en"));

        await Assert.That(viewModel.IsMinimized).IsFalse();

        await viewModel.MinimizeCommand.Execute();

        await Assert.That(viewModel.IsMinimized).IsTrue();
        await Assert.That(viewModel.Operation.IsRunning).IsTrue();
    }

    [Test]
    public async Task Restore_command_clears_is_minimized()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("discord", operation, new LocalizationService("en"));
        await viewModel.MinimizeCommand.Execute();

        await viewModel.RestoreCommand.Execute();

        await Assert.That(viewModel.IsMinimized).IsFalse();
    }

    [Test]
    public async Task Title_label_includes_the_package_name()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));
        var viewModel = new BuildJobViewModel("spotify-launcher", operation, new LocalizationService("en"));

        await Assert.That(viewModel.TitleLabel).IsEqualTo("Building spotify-launcher");
    }
}
