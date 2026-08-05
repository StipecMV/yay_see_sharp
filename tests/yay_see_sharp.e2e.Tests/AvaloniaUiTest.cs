using Avalonia.Headless;
using Avalonia.Threading;

namespace yay_see_sharp.e2e.Tests;

/// <summary>
/// Runs a test body on the single dedicated UI thread HeadlessUnitTestSession owns for this
/// assembly (Avalonia's headless platform, like the real one, is thread-affine — tests must not
/// touch it from whatever thread pool thread TUnit happened to schedule them on). Also flushes
/// the dispatcher after the body runs so layout/binding updates queued during the test are
/// actually applied before assertions that walk the visual tree run.
/// </summary>
public static class AvaloniaUiTest
{
    public static async Task RunAsync(Func<Task> body)
    {
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AvaloniaUiTest).Assembly).Dispatch(
            async () =>
            {
                await body();
                Pump();
                return true;
            },
            CancellationToken.None);
    }

    /// <summary>Flushes pending layout/binding/render jobs. Call after any state change a test then inspects via the visual tree — those updates are queued on the dispatcher, not applied synchronously.</summary>
    public static void Pump() => Dispatcher.UIThread.RunJobs();
}
