using System.Net.Http;
using log4net;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Http;

public sealed class PkgbuildService : IPkgbuildService
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PkgbuildService));
    private readonly HttpClient _httpClient;

    public PkgbuildService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient.Instance;
    }

    public async Task<string> FetchAsync(string packageName, PackageSource source, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(packageName, source);
        Log.Info($"Fetching PKGBUILD for {packageName} (source={source}) from {url}");
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warn($"PKGBUILD fetch for {packageName} returned HTTP {(int)response.StatusCode}");
        }

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
