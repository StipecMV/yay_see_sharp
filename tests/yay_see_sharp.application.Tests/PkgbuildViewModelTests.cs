using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

public class PkgbuildViewModelTests
{
    [Test]
    public async Task Load_populates_content_on_successful_fetch()
    {
        var service = new Mock<IPkgbuildService>();
        service.Setup(s => s.FetchAsync("firefox", PackageSource.Official, It.IsAny<CancellationToken>()))
            .ReturnsAsync("pkgname=firefox\npkgver=131.0");
        var viewModel = new PkgbuildViewModel("firefox", PackageSource.Official, service.Object, new LocalizationService("en"));

        await viewModel.LoadAsync();

        await Assert.That(viewModel.Content).IsEqualTo("pkgname=firefox\npkgver=131.0");
        await Assert.That(viewModel.ErrorMessage).IsNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task Load_delegates_to_the_service_with_the_requested_package_and_source()
    {
        var service = new Mock<IPkgbuildService>();
        service.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<PackageSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");
        var viewModel = new PkgbuildViewModel("spotify", PackageSource.Aur, service.Object, new LocalizationService("en"));

        await viewModel.LoadAsync();

        service.Verify(s => s.FetchAsync("spotify", PackageSource.Aur, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Load_sets_error_message_on_404_and_leaves_content_empty()
    {
        var service = new Mock<IPkgbuildService>();
        service.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<PackageSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("not found", null, HttpStatusCode.NotFound));
        var viewModel = new PkgbuildViewModel("nonexistent-package", PackageSource.Aur, service.Object, new LocalizationService("en"));

        await viewModel.LoadAsync();

        await Assert.That(viewModel.Content).IsNull();
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
        await Assert.That(viewModel.ErrorMessage!).Contains("404");
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task Load_sets_error_message_on_network_failure_instead_of_throwing()
    {
        var service = new Mock<IPkgbuildService>();
        service.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<PackageSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network unreachable"));
        var viewModel = new PkgbuildViewModel("firefox", PackageSource.Official, service.Object, new LocalizationService("en"));

        await viewModel.LoadAsync();

        await Assert.That(viewModel.ErrorMessage).IsEqualTo("network unreachable");
        await Assert.That(viewModel.Content).IsNull();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task Close_command_completes_the_wait_for_close_task()
    {
        var service = new Mock<IPkgbuildService>();
        service.Setup(s => s.FetchAsync(It.IsAny<string>(), It.IsAny<PackageSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("ok");
        var viewModel = new PkgbuildViewModel("firefox", PackageSource.Official, service.Object, new LocalizationService("en"));

        var waitTask = viewModel.WaitForCloseAsync();
        await viewModel.CloseCommand.Execute();

        await Assert.That(waitTask.IsCompleted).IsTrue();
    }

    [Test]
    public async Task Title_label_includes_the_package_name()
    {
        var service = new Mock<IPkgbuildService>();
        var viewModel = new PkgbuildViewModel("neovim", PackageSource.Aur, service.Object, new LocalizationService("en"));

        await Assert.That(viewModel.TitleLabel).IsEqualTo("PKGBUILD — neovim");
    }

    [Test]
    public async Task A_null_service_falls_back_to_a_real_pkgbuild_service_instead_of_throwing()
    {
        var viewModel = new PkgbuildViewModel("neovim", PackageSource.Aur, pkgbuildService: null, new LocalizationService("en"));

        await Assert.That(viewModel.TitleLabel).IsEqualTo("PKGBUILD — neovim");
    }
}
