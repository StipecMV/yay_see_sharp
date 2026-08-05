using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Settings;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class SettingsPersistenceIntegrationTests
{
    [Test]
    public async Task Settings_written_to_disk_are_read_back_identically_by_a_fresh_store_instance()
    {
        var path = Path.Combine(Path.GetTempPath(), "yss-integration-settings-" + Guid.NewGuid() + ".json");
        var written = new AppSettings(
            "sk",
            ThemePreference.Dark,
            CloseAction.Exit,
            NotificationsEnabled: false,
            RemoveOrphansByDefault: false,
            new TimeOnly(6, 45),
            PackageManagerEngine.Paru,
            "/tmp/build-dir",
            AutoUpdateCheckEnabled: false);

        try
        {
            var writer = new FileSettingsStore(path);
            await writer.SaveAsync(written);

            // A brand new instance, pointed at the same real file on disk — no shared in-memory state.
            var reader = new FileSettingsStore(path);
            var reloaded = await reader.LoadAsync();

            await Assert.That(reloaded.Language).IsEqualTo(written.Language);
            await Assert.That(reloaded.Theme).IsEqualTo(written.Theme);
            await Assert.That(reloaded.CloseAction).IsEqualTo(written.CloseAction);
            await Assert.That(reloaded.NotificationsEnabled).IsEqualTo(written.NotificationsEnabled);
            await Assert.That(reloaded.RemoveOrphansByDefault).IsEqualTo(written.RemoveOrphansByDefault);
            await Assert.That(reloaded.UpdateScheduleTime).IsEqualTo(written.UpdateScheduleTime);
            await Assert.That(reloaded.Engine).IsEqualTo(written.Engine);
            await Assert.That(reloaded.BuildDirectory).IsEqualTo(written.BuildDirectory);
            await Assert.That(reloaded.AutoUpdateCheckEnabled).IsEqualTo(written.AutoUpdateCheckEnabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task Loading_a_settings_file_that_does_not_exist_yet_falls_back_to_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "yss-integration-settings-missing-" + Guid.NewGuid() + ".json");
        await Assert.That(File.Exists(path)).IsFalse();

        var store = new FileSettingsStore(path);
        var settings = await store.LoadAsync();

        await Assert.That(settings).IsEqualTo(AppSettings.Default);
    }
}
