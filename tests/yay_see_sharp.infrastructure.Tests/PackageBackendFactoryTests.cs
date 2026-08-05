using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using yay_see_sharp.infrastructure.Platform;
using Moq;

public class PackageBackendFactoryTests
{
    [Test]
    public async Task Factory_returns_demo_backend_for_non_arch_distribution()
    {
        var detector = new Mock<IDistributionDetector>();
        var snapshot = new DistributionSnapshot("ubuntu", "Ubuntu", "GNOME", "wayland");
        var info = new BackendInfo("ubuntu", "Ubuntu", "demo", BackendMode.Demo, false);
        detector.Setup(item => item.Detect()).Returns(snapshot);
        detector.Setup(item => item.CreateBackendInfo(snapshot)).Returns(info);

        var factory = new PackageBackendFactory(detector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        await Assert.That(backend).IsTypeOf<DemoPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Demo);
    }

    [Test]
    public async Task Factory_returns_yay_backend_for_arch_distribution()
    {
        var detector = new Mock<IDistributionDetector>();
        var snapshot = new DistributionSnapshot("arch", "Arch Linux", "KDE", "wayland");
        var info = new BackendInfo("arch", "Arch Linux", "yay", BackendMode.Real, true);
        detector.Setup(item => item.Detect()).Returns(snapshot);
        detector.Setup(item => item.CreateBackendInfo(snapshot)).Returns(info);

        var factory = new PackageBackendFactory(detector.Object, Mock.Of<ICommandRunner>(), Mock.Of<IYayOutputParser>());
        var backend = factory.Create();

        await Assert.That(backend).IsTypeOf<YayPackageBackend>();
        await Assert.That(backend.Info.Mode).IsEqualTo(BackendMode.Real);
    }
}
