namespace yay_see_sharp.domain.Abstractions;

/// <summary>Live, user-configured AUR build directory, read by YayPackageBackend at install/update time without coupling it to SettingsViewModel directly.</summary>
public interface IBuildDirectoryPolicy
{
    /// <summary>May start with "~"; the backend expands it to the user's home directory before use.</summary>
    string BuildDirectory { get; }
}
