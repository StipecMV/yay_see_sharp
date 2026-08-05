using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.infrastructure;

public sealed class PackageBackendFactory : IPackageBackendFactory
{
    private readonly IDistributionDetector _distributionDetector;
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;
    private readonly IPrivilegeService? _privilegeService;

    public PackageBackendFactory(
        IDistributionDetector distributionDetector,
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        IPrivilegeService? privilegeService = null)
    {
        _distributionDetector = distributionDetector;
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
    }

    public IPackageBackend Create()
    {
        var snapshot = _distributionDetector.Detect();
        var info = _distributionDetector.CreateBackendInfo(snapshot);

        // TODO: paru support — add a ParuPackageBackend and branch on Settings.Engine once one
        // exists. Only yay is implemented today, so the real backend is always YayPackageBackend
        // regardless of the user's engine preference (see SettingsViewModel.EngineOptions, which
        // is limited to Yay for the same reason).
        return info.Mode == BackendMode.Real
            ? new YayPackageBackend(_commandRunner, _outputParser, info, _privilegeService)
            : new DemoPackageBackend(info);
    }
}
