using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public interface IYayOutputParser
{
    IReadOnlyList<PackageSummary> ParseSearch(string output);

    /// <param name="sourceHint">Used only as a fallback when the "Repository" field itself doesn't clearly indicate AUR — e.g. a `-Sia` (AUR sync-info) response, which may omit that field entirely.</param>
    PackageDetails? ParseInfo(string output, PackageSource? sourceHint = null);

    /// <param name="foreignPackageNames">Names from `pacman -Qm` (foreign/AUR-installed packages). A name present here is classified <see cref="PackageSource.Aur"/>; everything else is <see cref="PackageSource.Official"/>. Omitted/null when unavailable — everything then defaults to Official, same as before this classification existed.</param>
    IReadOnlyList<UpdateInfo> ParseUpdates(string output, IReadOnlySet<string>? foreignPackageNames = null);

    /// <param name="foreignPackageNames">See <see cref="ParseUpdates"/>.</param>
    IReadOnlyList<PackageSummary> ParseInstalled(string output, IReadOnlySet<string>? foreignPackageNames = null);
}
