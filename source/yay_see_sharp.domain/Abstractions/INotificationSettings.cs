namespace yay_see_sharp.domain.Abstractions;

/// <summary>Exposes the live, user-configured desktop-notifications toggle without coupling notification gating to SettingsViewModel directly.</summary>
public interface INotificationSettings
{
    bool NotificationsEnabled { get; }
}
