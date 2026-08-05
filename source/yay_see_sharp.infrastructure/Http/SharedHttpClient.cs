using System.Net.Http;

namespace yay_see_sharp.infrastructure.Http;

/// <summary>Process-wide HttpClient instance, shared by view models that fetch remote resources (e.g. PKGBUILD text) to avoid socket exhaustion from per-call instantiation.</summary>
public static class SharedHttpClient
{
    public static HttpClient Instance { get; } = new();
}
