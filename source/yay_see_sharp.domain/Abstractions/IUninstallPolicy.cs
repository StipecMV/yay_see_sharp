namespace yay_see_sharp.domain.Abstractions;

/// <summary>Exposes the live, user-configured default for whether uninstalling a package also removes its orphaned dependencies.</summary>
public interface IUninstallPolicy
{
    bool RemoveOrphansByDefault { get; }
}
