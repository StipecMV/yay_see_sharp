using System.Text.RegularExpressions;

namespace yay_see_sharp.infrastructure.Yay;

/// <summary>
/// Validates user-controlled values before they become `yay`/`pacman` CLI arguments.
/// <see cref="Process.SystemCommandRunner"/> already passes every argument through
/// <c>ProcessStartInfo.ArgumentList</c> with <c>UseShellExecute = false</c>, so none of this is
/// about shell command injection (no shell is ever spawned). It guards a narrower "argument
/// confusion" vector instead: a value starting with '-' being parsed by yay/pacman's own CLI as
/// an option instead of the positional package name or search term it was meant to be.
/// </summary>
internal static partial class PackageArgumentValidator
{
    [GeneratedRegex(@"^[a-zA-Z0-9@._+-]+$")]
    private static partial Regex PackageNamePattern();

    /// <summary>Arch package names: non-empty, not leading with '-' (which pacman/yay would read as an option), and restricted to the characters Arch's own naming policy allows.</summary>
    public static bool IsValidPackageName(string packageName) =>
        !string.IsNullOrWhiteSpace(packageName) &&
        !packageName.StartsWith('-') &&
        PackageNamePattern().IsMatch(packageName);

    /// <summary>Free-text search terms only need the narrower "doesn't look like an option" check — full package-name character restrictions don't apply to search queries.</summary>
    public static bool StartsWithOptionDash(string query) => query.TrimStart().StartsWith('-');
}
