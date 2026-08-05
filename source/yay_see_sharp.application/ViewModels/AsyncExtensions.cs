namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Deliberate fire-and-forget helpers for the handful of places a view model kicks off async work
/// from a constructor or a property setter without awaiting it (e.g. loading details right after
/// selecting a row). Bare `_ = SomeAsync();` silently drops any exception the task faults with;
/// this at least observes and reports it instead.
/// </summary>
internal static class AsyncExtensions
{
    public static void FireAndForget(this Task task) => task.ContinueWith(
        static completed => System.Diagnostics.Debug.WriteLine(
            $"Unobserved exception in fire-and-forget task: {completed.Exception}"),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
