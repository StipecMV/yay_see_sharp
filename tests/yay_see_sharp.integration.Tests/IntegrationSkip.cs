using System.Net.Sockets;
using TUnit.Core.Exceptions;

namespace yay_see_sharp.integration.Tests;

/// <summary>Shared skip helpers so every real-dependency test degrades to a clean "skipped" result instead of a failure when its dependency isn't available.</summary>
internal static class IntegrationSkip
{
    /// <summary>Same opt-in gate as the destructive Arch test in the unit test project — these tests install/uninstall real packages.</summary>
    public const string ArchGateVariable = "YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS";

    public static void ThrowIfArchGateNotSet()
    {
        if (Environment.GetEnvironmentVariable(ArchGateVariable) != "1")
        {
            throw new SkipTestException(
                $"Set {ArchGateVariable}=1 on an Arch/CachyOS host with yay installed to run this destructive integration test.");
        }

        if (!IsOnPath("yay"))
        {
            throw new SkipTestException("yay was not found on PATH.");
        }
    }

    public static bool IsOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, executable)));
    }

    /// <summary>Runs a real network call and turns any failure (DNS, timeout, refused, ...) into a graceful skip rather than a test failure.</summary>
    public static async Task<T> RunOrSkipOnNetworkFailureAsync<T>(Func<Task<T>> action, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await action().WaitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or SocketException)
        {
            throw new SkipTestException($"Network unavailable or too slow for this test: {ex.Message}");
        }
    }
}
