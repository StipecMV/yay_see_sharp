using System;
using System.IO;
using System.Threading.Tasks;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Settings;

public class FileSettingsStoreTests
{
    [Test]
    public async Task Load_returns_defaults_when_no_file_exists()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);

        var settings = await store.LoadAsync();

        await Assert.That(settings).IsEqualTo(AppSettings.Default);
    }

    [Test]
    public async Task Save_then_load_roundtrips_settings()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var store = new FileSettingsStore(path);
        var settings = new AppSettings("sk", ThemePreference.Dark, CloseAction.Exit, false, false, new TimeOnly(14, 30));

        try
        {
            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            await Assert.That(loaded).IsEqualTo(settings);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
