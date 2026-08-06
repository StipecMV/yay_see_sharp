using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using yay_see_sharp.infrastructure.Platform;
using Moq;

namespace yay_see_sharp.infrastructure.Tests;

public class PackageBackendFactoryTests
{
    private static Mock<IDistributionDetector> CreateDetector(
        DistributionSnapshot snapshot, BackendInfo whenYayAvailable, BackendInfo whenYayUnavailable)
    {
        var detector = new Mock<IDistributionDetector>();
        detector.Setup(item => item.Detect()).Returns(snapshot);
        detector.Setup(item => item.CreateBackendInfo(snapshot, true)).Returns(whenYayAvailable);
        detector.Setup(item => item.CreateBackendInfo(snapshot, false)).Returns(whenYayUnavailable);
        return detector;
    }

    private static Mock<IEngineDetector> CreateEngineDetector(PackageManagerEngine? detected)
    {
        var engineDetector = new Mock<IEngineDetector>();
        engineDetector.Setup(item => item.Detect()).Returns(detected);
        return engineDetector;
    }

    [Test]
    public async Task Arch_with_yay_on_path_selects_real_yay_backend()
    {
        var snapshot = new DistributionSnapshot("arch", "Arch Linux", "KDE", "wayland");
        var real = new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true);
        var unavailable = new BackendInfo("arch", "Arch Linux", "demo", BackendMode.Unavailable, false, "yay missing");
        var detector = CreateDetector(snapshot, real, unavailable);
        var engineDetector = CreateEngineDetector(PackageManagerEngine.Yay);

        var factory = new PackageBackendFactory(
            detector.Object, engineDetector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        await Assert.That(backend).IsTypeOf<YayPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Real);
    }

    [Test]
    public async Task Arch_without_yay_on_path_falls_back_to_a_safe_demo_backed_unavailable_state()
    {
        var snapshot = new DistributionSnapshot("arch", "Arch Linux", "KDE", "wayland");
        var real = new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true);
        var unavailable = new BackendInfo("arch", "Arch Linux", "demo", BackendMode.Unavailable, false, "yay missing");
        var detector = CreateDetector(snapshot, real, unavailable);
        var engineDetector = CreateEngineDetector(null);

        var factory = new PackageBackendFactory(
            detector.Object, engineDetector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        // Unavailable must never silently behave like Real — it falls back to the same safe,
        // non-mutating backend as Demo mode, while still reporting Mode == Unavailable so the UI
        // can offer to install the missing binary instead of pretending everything is fine.
        await Assert.That(backend).IsTypeOf<DemoPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Unavailable);
        await Assert.That(backend.Info.IsSupported).IsFalse();
    }

    [Test]
    public async Task CachyOS_with_yay_on_path_selects_real_yay_backend()
    {
        var snapshot = new DistributionSnapshot("cachyos", "CachyOS", "KDE", "wayland");
        var real = new BackendInfo("cachyos", "CachyOS", "yay", BackendMode.Real, true);
        var unavailable = new BackendInfo("cachyos", "CachyOS", "demo", BackendMode.Unavailable, false);
        var detector = CreateDetector(snapshot, real, unavailable);
        var engineDetector = CreateEngineDetector(PackageManagerEngine.Yay);

        var factory = new PackageBackendFactory(
            detector.Object, engineDetector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        await Assert.That(backend).IsTypeOf<YayPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Real);
    }

    [Test]
    public async Task Non_arch_distribution_selects_demo_backend_even_when_yay_is_on_path()
    {
        var snapshot = new DistributionSnapshot("ubuntu", "Ubuntu", "GNOME", "wayland");
        var demo = new BackendInfo("ubuntu", "Ubuntu", "demo", BackendMode.Demo, false);
        var detector = new Mock<IDistributionDetector>();
        detector.Setup(item => item.Detect()).Returns(snapshot);
        detector.Setup(item => item.CreateBackendInfo(snapshot, It.IsAny<bool>())).Returns(demo);
        var engineDetector = CreateEngineDetector(PackageManagerEngine.Yay);

        var factory = new PackageBackendFactory(
            detector.Object, engineDetector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        await Assert.That(backend).IsTypeOf<DemoPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Demo);
    }

    [Test]
    public async Task Factory_uses_the_engine_detector_as_the_single_source_of_truth_for_yay_availability()
    {
        var snapshot = new DistributionSnapshot("arch", "Arch Linux", "KDE", "wayland");
        var real = new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true);
        var unavailable = new BackendInfo("arch", "Arch Linux", "demo", BackendMode.Unavailable, false);
        var detector = CreateDetector(snapshot, real, unavailable);
        var engineDetector = CreateEngineDetector(PackageManagerEngine.Yay);

        var factory = new PackageBackendFactory(
            detector.Object, engineDetector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        factory.Create();

        engineDetector.Verify(item => item.Detect(), Times.Once);
        detector.Verify(item => item.CreateBackendInfo(snapshot, true), Times.Once);
    }
}
