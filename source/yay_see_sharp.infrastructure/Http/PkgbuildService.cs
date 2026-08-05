using System.Net.Http;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Http;

public sealed class PkgbuildService : IPkgbuildService
{
    private readonly HttpClient _httpClient;

    public PkgbuildService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient.Instance;
    }

    public async Task<string> FetchAsync(string packageName, PackageSource source, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(BuildUrl(packageName, source), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string BuildUrl(string packageName, PackageSource source)
    {
        var escaped = Uri.EscapeDataString(packageName);
        return source == PackageSource.Aur
            ? $"https://aur.archlinux.org/cgit/aur.git/plain/PKGBUILD?h={escaped}"
            : $"https://gitlab.archlinux.org/archlinux/packaging/packages/{escaped}/-/raw/main/PKGBUILD";
    }
}
