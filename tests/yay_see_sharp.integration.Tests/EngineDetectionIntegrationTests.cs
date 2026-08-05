using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class EngineDetectionIntegrationTests
{
    [Test]
    public async Task Detect_scans_the_real_path_and_returns_yay_paru_or_none_without_throwing()
    {
        var detector = new EngineDetector();

        var result = detector.Detect();

        var isValid = result is null or PackageManagerEngine.Yay or PackageManagerEngine.Paru;
        await Assert.That(isValid).IsTrue();
    }

    [Test]
    public async Task Detect_matches_a_plain_path_scan_for_yay()
    {
        var detector = new EngineDetector();
        var yayOnPath = IntegrationSkip.IsOnPath("yay");

        var result = detector.Detect();

        if (yayOnPath)
        {
            await Assert.That(result).IsEqualTo(PackageManagerEngine.Yay);
        }
        else if (!IntegrationSkip.IsOnPath("paru"))
        {
            await Assert.That(result).IsNull();
        }
    }
}
