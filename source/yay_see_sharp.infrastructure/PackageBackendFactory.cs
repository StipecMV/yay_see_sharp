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
    private readonly IEnginePreference? _enginePreference;

    public PackageBackendFactory(
        IDistributionDetector distributionDetector,
        IEngineDetector engineDetector,
        ICommandRunner commandRunner,
        IYayOutputParser outputParser,
        IPrivilegeService? privilegeService = null,
        IBuildDirectoryPolicy? buildDirectoryPolicy = null,
        IEnginePreference? enginePreference = null)
    {
        _distributionDetector = distributionDetector;
        _engineDetector = engineDetector;
        _commandRunner = commandRunner;
        _outputParser = outputParser;
        _privilegeService = privilegeService;
        _buildDirectoryPolicy = buildDirectoryPolicy;
        _enginePreference = enginePreference;
    }

    public IPackageBackend Create()
    {
        var snapshot = _distributionDetector.Detect();

        // PARU-2026-08: the preferred engine comes from Settings (yay by default when nothing is
        // wired up). EngineDetector stays the single source of truth for "is the engine actually
        // usable" — distribution identity only says which OS this is, never what's on PATH, so
        // real-mode eligibility always flows through this one check against the *preferred*
        // engine (a system with only paru installed and yay selected is Unavailable, not Real).
        var preferredEngine = _enginePreference?.Engine ?? PackageManagerEngine.Yay;
        var preferredAvailable = _engineDetector.Detect() == preferredEngine;
        var info = _distributionDetector.CreateBackendInfo(snapshot, preferredAvailable);

        // The detector only knows booleans and hardcodes "yay" — the factory corrects the
        // reported package manager + warning so the UI/logs name the engine that actually runs.
        if (info.Mode == BackendMode.Real)
        {
            info = info with { PackageManager = preferredEngine == PackageManagerEngine.Paru ? "paru" : "yay" };
        }
        else if (info.Mode == BackendMode.Unavailable && preferredEngine == PackageManagerEngine.Paru)
        {
            info = info with
            {
                Warning = "paru was not found on PATH. Install it (or switch the engine back to yay in Settings) to use Real mode, or continue in Demo mode.",
            };
        }

        // Log the choice so the message carries the actual mode selected.
        Log.Info(
            $"Backend selected: mode={info.Mode} packageManager={info.PackageManager} distribution={info.DistributionId} ({info.DistributionName})" +
            (string.IsNullOrWhiteSpace(info.Warning) ? string.Empty : $" — {info.Warning}"));

        return info.Mode == BackendMode.Real
            ? new YayPackageBackend(_commandRunner, _outputParser, info, _privilegeService, buildDirectoryPolicy: _buildDirectoryPolicy, engine: preferredEngine)
            : new DemoPackageBackend(info);
    }
}
