namespace yay_see_sharp.domain.Models;

public enum CloseAction
{
    HideToTray,
    Exit,
}

public enum PackageManagerEngine
{
    Yay,
    Paru,
}

public sealed record AppSettings(
    string Language,
    ThemePreference Theme,
    CloseAction CloseAction,
    bool NotificationsEnabled,
    bool RemoveOrphansByDefault,
    TimeOnly UpdateScheduleTime,
    PackageManagerEngine Engine = PackageManagerEngine.Yay,
    string BuildDirectory = "~/.cache/yay",
    bool AutoUpdateCheckEnabled = true)
{
    public static AppSettings Default { get; } = new(
        "en",
        ThemePreference.System,
        CloseAction.HideToTray,
        true,
        true,
        new TimeOnly(10, 0));
}
