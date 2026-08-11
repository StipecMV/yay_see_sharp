using log4net;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Process;
using yay_see_sharp.infrastructure.Yay;
using yay_see_sharp.infrastructure.Platform;

namespace yay_see_sharp.infrastructure;

public sealed class PackageBackendFactory : IPackageBackendFactory
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PackageBackendFactory));
    private readonly IDistributionDetector _distributionDetector;
    private readonly IEngineDetector _engineDetector;
    private readonly ICommandRunner _commandRunner;
    private readonly IYayOutputParser _outputParser;
    private readonly IPrivilegeService? _privilegeService;
    private readonly IBuildDirectoryPolicy? _buildDirectoryPolicy;

    public PackageBackendFactory(
        IDistributionDetector distributionDetector,
        IEngineDetector engineDetector,
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        IPrivilegeService? privilegeService = null,
        IBuildDirectoryPolicy? buildDirectoryPolicy = null)
    {
        _distributionDetector = distributionDetector;
        _engineDetector = engineDetector;
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
        _buildDirectoryPolicy = buildDirectoryPolicy;
    }

    public IPackageBackend Create()
    {
        var snapshot = _distributionDetector.Detect();

        // EngineDetector is the single source of truth for "is yay actually usable" — the
        // distribution detector only knows the OS identity, never the PATH itself, so real-mode
        // eligibility always flows through this one check.
        var yayAvailable = _engineDetector.Detect() == PackageManagerEngine.Yay;
        var info = _distributionDetector.CreateBackendInfo(snapshot, yayAvailable);

        // TODO: paru support — add a ParuPackageBackend and branch on Settings.Engine once one
        // exists. Only yay is implemented today, so the real backend is always YayPackageBackend
        // regardless of the user's engine preference (see SettingsViewModel.EngineOptions, which
        // is limited to Yay for the same reason).
        //
        // BackendMode.Unavailable (Arch/CachyOS detected but yay missing from PATH) falls back to
        // the same safe Demo-backed instance as a non-Arch host — Info.Mode still reports
        // Unavailable so the UI can offer to install the real backend, but no yay/pacman command
        // ever gets run against a binary we just confirmed doesn't exist.
        // Log the choice so the message carries the actual mode selected.
        Log.Info(
            $"Backend selected: mode={info.Mode} packageManager={info.PackageManager} distribution={info.DistributionId} ({info.DistributionName})" +
            (string.IsNullOrWhiteSpace(info.Warning) ? string.Empty : $" — {info.Warning}"));

        return info.Mode == BackendMode.Real
            ? new YayPackageBackend(_commandRunner, _outputParser, info, _privilegeService, buildDirectoryPolicy: _buildDirectoryPolicy)
            : new DemoPackageBackend(info);
    }
}
