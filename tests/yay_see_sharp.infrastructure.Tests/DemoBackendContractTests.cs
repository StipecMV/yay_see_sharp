using System.Collections.Generic;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Demo;

[InheritsTests]
public sealed class DemoBackendContractTests : PackageBackendContractTestsBase
{
    protected override ContractPackages Packages { get; } = new(
        NotInstalled: "hello",
        Installed: "git",
        Updatable: "firefox",
        Invalid: "does-not-exist-package",
        Failing: "spotify");

    protected override IPackageBackend CreateBackend(string? failingPackageName = null) => new DemoPackageBackend(
        simulatedFailures: failingPackageName is null ? null : new HashSet<string> { failingPackageName });
}
