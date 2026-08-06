using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Yay;

namespace yay_see_sharp.infrastructure.Tests;

[InheritsTests]
public sealed class YayBackendContractTests : PackageBackendContractTestsBase
{
    protected override ContractPackages Packages { get; } = new(
        NotInstalled: "yay-not-installed",
        Installed: "yay-installed",
        Updatable: "yay-updatable",
        Invalid: FakeYayCommandRunner.InvalidPackageName,
        Failing: "yay-failing-package");

    protected override IPackageBackend CreateBackend(string? failingPackageName = null) => new YayPackageBackend(
        new FakeYayCommandRunner(
            initiallyInstalled: [Packages.Installed, Packages.Updatable],
            failingPackageName: failingPackageName),
        new YayOutputParser());
}
