using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

/// <summary>Fetches raw PKGBUILD text, kept behind an interface so PkgbuildViewModel never owns an HttpClient or a URL pattern directly.</summary>
public interface IPkgbuildService
{
    Task<string> FetchAsync(string packageName, PackageSource source, CancellationToken cancellationToken = default);
}
