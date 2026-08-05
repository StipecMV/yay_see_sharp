namespace yay_see_sharp.domain.Abstractions;

/// <summary>Exposes the live, user-configured schedule for IUpdateScheduler without coupling it to SettingsViewModel directly.</summary>
public interface IUpdateScheduleSettings
{
    bool AutoUpdateCheckEnabled { get; }

    TimeOnly UpdateScheduleTime { get; }
}
