using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

/// <summary>The user's chosen package-manager engine (yay or paru). Consumed by
/// <c>PackageBackendFactory</c> so the real backend is built for the engine the user actually
/// selected in Settings, not always for yay.</summary>
public interface IEnginePreference
{
    PackageManagerEngine Engine { get; }
}
