using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Process;

/// <summary>
/// Stateful ICommandRunner double that recognizes the exact argument shapes YayPackageBackend's
/// Install/Uninstall/Update emit, so YayPackageBackend can be exercised in contract tests without a
/// real yay binary. It is not a general yay simulator — only the commands those three operations
/// issue are understood; anything else throws.
/// </summary>
public sealed class FakeYayCommandRunner : ICommandRunner
{
    /// <summary>A package name no repository would ever contain, used to simulate "target not found".</summary>
    public const string InvalidPackageName = "yay-invalid-package";

    private readonly HashSet<string> _installed;
    private readonly string? _failingPackageName;

    public FakeYayCommandRunner(IEnumerable<string>? initiallyInstalled = null, string? failingPackageName = null)
    {
        _installed = new HashSet<string>(initiallyInstalled ?? [], StringComparer.OrdinalIgnoreCase);
        _failingPackageName = failingPackageName;
    }

    public IReadOnlyCollection<string> Installed => _installed;

    public Task<CommandResult> RunAsync(
        CommandRequest request,
        IProgress<CommandOutput>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new CommandResult(-1, [], true));
        }

        var args = request.Arguments;

        if (args.Count == 4 && args[0] == "--needed" && args[2] == "-S")
        {
            return Task.FromResult(RunInstall(args[3]));
        }

        if (args.Count == 3 && args[0] == "--noconfirm" && (args[1] == "-Rns" || args[1] == "-Rn"))
        {
            return Task.FromResult(RunUninstall(args[2]));
        }

        if (args.Count == 2 && args[0] == "-Syu")
        {
            return Task.FromResult(RunFullUpdate());
        }

        if (args.Count >= 4 && args[0] == "-S" && args[1] == "--noconfirm" && args[2] == "--needed")
        {
            return Task.FromResult(RunSelectiveUpdate(args.Skip(3).ToArray()));
        }

        throw new NotSupportedException($"FakeYayCommandRunner does not simulate: {string.Join(' ', args)}");
    }

    private CommandResult RunInstall(string package)
    {
        if (IsFailing(package))
        {
            return Failure($"error: target not found: {package}");
        }

        _installed.Add(package);
        return Success($"installing {package}...");
    }

    private CommandResult RunUninstall(string package)
    {
        if (IsFailing(package))
        {
            return Failure($"error: failed to remove {package}");
        }

        _installed.Remove(package);
        return Success($"removing {package}...");
    }

    private CommandResult RunFullUpdate()
    {
        if (_failingPackageName is not null)
        {
            return Failure("error: failed to synchronize all databases");
        }

        return Success("upgrading system...");
    }

    private CommandResult RunSelectiveUpdate(IReadOnlyList<string> packages)
    {
        if (packages.Any(IsFailing))
        {
            return Failure($"error: target not found: {packages.First(IsFailing)}");
        }

        return Success($"upgrading {string.Join(' ', packages)}...");
    }

    private bool IsFailing(string package) => package == InvalidPackageName || package == _failingPackageName;

    private static CommandResult Success(string message) => new(
        0, [new CommandOutput(CommandOutputKind.StandardOutput, message, DateTimeOffset.UtcNow)], false);

    private static CommandResult Failure(string message) => new(
        1, [new CommandOutput(CommandOutputKind.StandardError, message, DateTimeOffset.UtcNow)], false);
}
