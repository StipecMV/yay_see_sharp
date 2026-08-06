using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

public interface IEngineDetector
{
    PackageManagerEngine? Detect();
}
