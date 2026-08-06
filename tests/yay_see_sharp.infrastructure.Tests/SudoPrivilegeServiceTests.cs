using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Privilege;

namespace yay_see_sharp.infrastructure.Tests;

public class SudoPrivilegeServiceTests
{
    [Test]
    public async Task Request_elevation_grants_immediately_when_the_sudo_timestamp_is_already_valid()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new SudoPrivilegeService(invoker.Object)
        {
            PasswordPrompt = _ => throw new InvalidOperationException("should not be asked for a password"),
        };

        var result = await service.RequestElevationAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Granted);
        await Assert.That(service.IsElevated).IsTrue();
        invoker.Verify(i => i.RefreshWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Request_elevation_prompts_for_a_password_and_grants_when_it_is_accepted()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        invoker.Setup(i => i.RefreshWithPasswordAsync("hunter2", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new SudoPrivilegeService(invoker.Object) { PasswordPrompt = _ => Task.FromResult<string?>("hunter2") };

        var result = await service.RequestElevationAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Granted);
        await Assert.That(service.IsElevated).IsTrue();
        invoker.Verify(i => i.RefreshWithPasswordAsync("hunter2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Request_elevation_returns_cancelled_when_the_prompt_is_dismissed_without_a_password()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new SudoPrivilegeService(invoker.Object) { PasswordPrompt = _ => Task.FromResult<string?>(null) };

        var result = await service.RequestElevationAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Cancelled);
        await Assert.That(service.IsElevated).IsFalse();
        invoker.Verify(i => i.RefreshWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Request_elevation_returns_failed_when_the_password_is_rejected()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        invoker.Setup(i => i.RefreshWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new SudoPrivilegeService(invoker.Object) { PasswordPrompt = _ => Task.FromResult<string?>("wrong-password") };

        var result = await service.RequestElevationAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Failed);
        await Assert.That(service.IsElevated).IsFalse();
    }

    [Test]
    public async Task Request_elevation_reprompts_when_a_previously_granted_timestamp_has_since_expired()
    {
        var invoker = new Mock<ISudoInvoker>();
        // Never reports a cached-valid timestamp, simulating the "expired every time" case: each
        // RequestElevationAsync call below has to go through a fresh password prompt.
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        invoker.Setup(i => i.RefreshWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var promptedPasswords = new List<string?>();
        var service = new SudoPrivilegeService(invoker.Object)
        {
            PasswordPrompt = _ =>
            {
                var password = promptedPasswords.Count == 0 ? "first" : "second";
                promptedPasswords.Add(password);
                return Task.FromResult<string?>(password);
            },
        };

        var firstResult = await service.RequestElevationAsync();
        var secondResult = await service.RequestElevationAsync();

        await Assert.That(firstResult).IsEqualTo(PrivilegeResult.Granted);
        await Assert.That(secondResult).IsEqualTo(PrivilegeResult.Granted);
        await Assert.That(service.IsElevated).IsTrue();
        await Assert.That(promptedPasswords).IsEquivalentTo(new List<string?> { "first", "second" });
        invoker.Verify(i => i.RefreshWithPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Request_elevation_fails_closed_when_no_password_prompt_has_been_wired_up_yet()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new SudoPrivilegeService(invoker.Object);

        var result = await service.RequestElevationAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Failed);
        await Assert.That(service.IsElevated).IsFalse();
    }

    [Test]
    public async Task Refresh_if_needed_is_a_no_op_cancellation_when_nothing_was_ever_granted()
    {
        var invoker = new Mock<ISudoInvoker>();
        var service = new SudoPrivilegeService(invoker.Object);

        var result = await service.RefreshIfNeededAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Cancelled);
        invoker.Verify(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Refresh_if_needed_extends_an_already_granted_elevation_without_prompting()
    {
        var invoker = new Mock<ISudoInvoker>();
        invoker.Setup(i => i.ValidateTimestampAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var service = new SudoPrivilegeService(invoker.Object)
        {
            PasswordPrompt = _ => throw new InvalidOperationException("should not be asked for a password"),
        };
        await service.RequestElevationAsync();

        var result = await service.RefreshIfNeededAsync();

        await Assert.That(result).IsEqualTo(PrivilegeResult.Granted);
        await Assert.That(service.IsElevated).IsTrue();
    }
}
