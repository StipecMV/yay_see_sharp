using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class OperationViewModelTests
{
    [Test]
    public async Task Apply_updates_progress_and_aggregates_output()
    {
        var operation = new OperationViewModel(PackageOperationKind.Install, new LocalizationService("en"));

        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Preparing, 5, "Preparing", "yay -S hello", "line 1"));
        operation.Apply(new PackageOperationProgress(
            PackageOperationKind.Install, PackageOperationStage.Completed, 100, "Installed", "yay -S hello", "line 2"));

        await Assert.That(operation.Stage).IsEqualTo(PackageOperationStage.Completed);
        await Assert.That(operation.Percent).IsEqualTo(100);
        await Assert.That(operation.Command).IsEqualTo("yay -S hello");
        await Assert.That(operation.OutputText).Contains("line 1");
        await Assert.That(operation.OutputText).Contains("line 2");
        await Assert.That(operation.IsRunning).IsFalse();
    }

    [Test]
    public async Task Stage_label_and_cancel_label_switch_language_live()
    {
        var localization = new LocalizationService("en");
        var operation = new OperationViewModel(PackageOperationKind.Install, localization);
        operation.Apply(new PackageOperationProgress(PackageOperationKind.Install, PackageOperationStage.Downloading, 40, "Downloading"));

        await Assert.That(operation.StageLabel).IsEqualTo("Downloading");
        await Assert.That(operation.CancelLabel).IsEqualTo("Cancel");

        localization.SetLanguage("sk");

        await Assert.That(operation.StageLabel).IsEqualTo("Sťahovanie");
        await Assert.That(operation.CancelLabel).IsEqualTo("Zrušiť");
    }
}
