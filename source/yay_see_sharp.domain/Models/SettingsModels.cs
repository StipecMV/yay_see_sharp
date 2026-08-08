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
        // BUGFIX-2026-08: desktop (OS-level) notifications are off by default — operation
        // results already surface as in-app toasts, and two popups for one event (system + app)
        // was confusing. The Settings toggle re-enables OS notifications for those who want them.
        false,
        true,
        new TimeOnly(10, 0));
}
