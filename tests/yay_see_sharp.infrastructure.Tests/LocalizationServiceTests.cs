using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Localization;

public class LocalizationServiceTests
{
    [Test]
    public async Task Unsupported_requested_language_falls_back_to_english()
    {
        var service = new LocalizationService("fr");

        await Assert.That(service.Language).IsEqualTo("en");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Dashboard");
    }

    [Test]
    public async Task SetLanguage_to_slovak_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("sk");

        await Assert.That(service.Language).IsEqualTo("sk");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Prehľad");
    }

    [Test]
    public async Task SetLanguage_raises_language_changed_event()
    {
        var service = new LocalizationService("en");
        var raised = false;
        service.LanguageChanged += (_, _) => raised = true;

        service.SetLanguage("sk");

        await Assert.That(raised).IsTrue();
    }

    [Test]
    public async Task Missing_key_falls_back_to_key_itself()
    {
        var service = new LocalizationService("en");

        await Assert.That(service.GetString("Unknown.Key")).IsEqualTo("Unknown.Key");
    }
}
