using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public interface IYayOutputParser
{
    IReadOnlyList<PackageSummary> ParseSearch(string output);

    /// <param name="sourceHint">Used only as a fallback when the "Repository" field itself doesn't clearly indicate AUR — e.g. a `-Sia` (AUR sync-info) response, which may omit that field entirely.</param>
    PackageDetails? ParseInfo(string output, PackageSource? sourceHint = null);

    /// <param name="foreignPackageNames">Names from `pacman -Qm` (foreign/not-in-any-configured-repo packages). A name present here and absent from <paramref name="confirmedAurPackageNames"/> is classified <see cref="PackageSource.Foreign"/>; everything else is <see cref="PackageSource.Official"/>. Omitted/null when unavailable — everything then defaults to Official, same as before this classification existed.</param>
    /// <param name="confirmedAurPackageNames">Names verified against AUR metadata (e.g. via a bulk `yay -Si` query) — classified <see cref="PackageSource.Aur"/>. Must be a subset of <paramref name="foreignPackageNames"/>; a name here always wins over the plain foreign classification.</param>
    IReadOnlyList<UpdateInfo> ParseUpdates(
        string output,
        IReadOnlySet<string>? foreignPackageNames = null,
        IReadOnlySet<string>? confirmedAurPackageNames = null);

    /// <param name="foreignPackageNames">See <see cref="ParseUpdates"/>.</param>
    /// <param name="confirmedAurPackageNames">See <see cref="ParseUpdates"/>.</param>
    IReadOnlyList<PackageSummary> ParseInstalled(
        string output,
        IReadOnlySet<string>? foreignPackageNames = null,
        IReadOnlySet<string>? confirmedAurPackageNames = null);

    /// <summary>Parses a bulk `yay -Si -- &lt;names...&gt;` response (blank-line-separated info blocks) into the set of package names whose Repository field indicates AUR. Used to confirm which foreign (`pacman -Qm`) names are actually AUR rather than some other out-of-repo source.</summary>
    IReadOnlySet<string> ParseAurConfirmedNames(string output);
}
