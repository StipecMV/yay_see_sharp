using System.Net.Http;

namespace yay_see_sharp.infrastructure.Http;

/// <summary>Process-wide HttpClient instance, shared by view models that fetch remote resources (e.g. PKGBUILD text) to avoid socket exhaustion from per-call instantiation.</summary>
public static class SharedHttpClient
{
    /// <summary>Explicit rather than the 100s BCL default — a PKGBUILD fetch that hasn't completed in 15s against a small text file almost certainly means a stuck connection, and the UI has no other way to bound how long the modal can spin.</summary>
    public static HttpClient Instance { get; } = new() { Timeout = TimeSpan.FromSeconds(15) };
}
