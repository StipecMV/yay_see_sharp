using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class DemoEndToEndTests
{
    [Test]
    public async Task Search_select_install_and_uninstall_workflow_reflects_in_demo_state()
    {
        var backend = new DemoPackageBackend();
        var search = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "hello" };

        await search.SearchCommand.Execute();
        await Assert.That(search.Results.Count).IsGreaterThan(0);

        search.SelectedPackage = search.Results[0];
        await Assert.That(search.SelectedDetails).IsNotNull();

        var details = search.SelectedDetails!;
        await details.LoadAsync();
        await Assert.That(details.Details!.Summary.State).IsEqualTo(PackageState.NotInstalled);

        await details.InstallCommand.Execute();
        await Assert.That(details.Operation).IsNull();
        await Assert.That(details.Details!.Summary.State).IsEqualTo(PackageState.Installed);

        await search.SearchCommand.Execute();
        var afterInstall = search.Results.Single(package => package.Name == details.Summary.Name);
        await Assert.That(afterInstall.State).IsEqualTo(PackageState.Installed);

        await details.UninstallCommand.Execute();
        await Assert.That(details.Operation).IsNull();
        await Assert.That(details.Details!.Summary.State).IsEqualTo(PackageState.NotInstalled);

        await search.SearchCommand.Execute();
        var afterUninstall = search.Results.Single(package => package.Name == details.Summary.Name);
        await Assert.That(afterUninstall.State).IsEqualTo(PackageState.NotInstalled);
    }
}
