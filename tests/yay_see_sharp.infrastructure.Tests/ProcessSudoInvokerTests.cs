using System.Threading;
using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Privilege;

namespace yay_see_sharp.infrastructure.Tests;

/// <summary>
/// Exercises the real `sudo` binary directly (like CommandRunnerTests does for `sh`) rather than
/// through a mock — `sudo -n -v` never prompts and never mutates anything (it only reports whether
/// a cached timestamp exists), so it's safe to run unconditionally on any host with sudo
/// installed, which this project already requires.
///
/// Deliberately not covered here: RefreshWithPasswordAsync with an actual password. Feeding it a
/// wrong password to test the failure path would trigger a real PAM authentication attempt on
/// whatever host runs this suite — repeated automated runs risk tripping pam_faillock account
/// lockout on a CI runner. That path is covered at the SudoPrivilegeService level instead, against
/// a fake ISudoInvoker.
/// </summary>
public class ProcessSudoInvokerTests
{
    [Test]
    public async Task ValidateTimestampAsync_returns_false_without_throwing_when_the_token_is_already_cancelled()
    {
        var invoker = new ProcessSudoInvoker();
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        var result = await invoker.ValidateTimestampAsync(cts.Token);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task ValidateTimestampAsync_is_stable_across_back_to_back_calls_on_a_live_token()
    {
        var invoker = new ProcessSudoInvoker();

        // Whether this host happens to have a cached sudo timestamp varies — the check itself
        // doesn't change that state, so two calls in a row must agree with each other regardless.
        var first = await invoker.ValidateTimestampAsync(CancellationToken.None);
        var second = await invoker.ValidateTimestampAsync(CancellationToken.None);

        await Assert.That(second).IsEqualTo(first);
    }
}
