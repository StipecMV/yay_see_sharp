using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Http;

public class PkgbuildServiceTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("pkgname=hello"),
            });
        }
    }

    [Test]
    public async Task FetchAsync_builds_the_aur_cgit_url_with_the_package_name_as_a_query_parameter()
    {
        var handler = new RecordingHandler();
        var service = new PkgbuildService(new HttpClient(handler));

        await service.FetchAsync("hello", PackageSource.Aur);

        await Assert.That(handler.RequestedUri!.ToString())
            .IsEqualTo("https://aur.archlinux.org/cgit/aur.git/plain/PKGBUILD?h=hello");
    }

    [Test]
    public async Task FetchAsync_builds_the_official_repo_url_with_the_package_name_as_a_path_segment()
    {
        var handler = new RecordingHandler();
        var service = new PkgbuildService(new HttpClient(handler));

        await service.FetchAsync("hello", PackageSource.Official);

        await Assert.That(handler.RequestedUri!.ToString())
            .IsEqualTo("https://gitlab.archlinux.org/archlinux/packaging/packages/hello/-/raw/main/PKGBUILD");
    }

    [Test]
    public async Task FetchAsync_percent_encodes_a_package_name_that_contains_url_metacharacters()
    {
        var handler = new RecordingHandler();
        var service = new PkgbuildService(new HttpClient(handler));

        await service.FetchAsync("hello&h=other/../secret", PackageSource.Aur);

        var query = handler.RequestedUri!.Query;
        await Assert.That(query).DoesNotContain("&h=other");
        await Assert.That(handler.RequestedUri.ToString()).Contains(Uri.EscapeDataString("hello&h=other/../secret"));
    }

    [Test]
    public async Task FetchAsync_returns_the_response_body_on_success()
    {
        var handler = new RecordingHandler();
        var service = new PkgbuildService(new HttpClient(handler));

        var content = await service.FetchAsync("hello", PackageSource.Aur);

        await Assert.That(content).IsEqualTo("pkgname=hello");
    }
}
