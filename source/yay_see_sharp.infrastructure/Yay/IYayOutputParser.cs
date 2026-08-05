using yay_see_sharp.domain.Models;

namespace yay_see_sharp.infrastructure.Yay;

public interface IYayOutputParser
{
    IReadOnlyList<PackageSummary> ParseSearch(string output);

    PackageDetails? ParseInfo(string output);

    IReadOnlyList<UpdateInfo> ParseUpdates(string output);

    IReadOnlyList<PackageSummary> ParseInstalled(string output);
}
