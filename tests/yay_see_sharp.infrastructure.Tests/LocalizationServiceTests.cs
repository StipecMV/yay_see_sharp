using System.Threading.Tasks;
using yay_see_sharp.infrastructure.Localization;

namespace yay_see_sharp.infrastructure.Tests;

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
    public async Task SetLanguage_to_german_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("de");

        await Assert.That(service.Language).IsEqualTo("de");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Übersicht");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Installieren");
        await Assert.That(service.GetString("Settings.Language.De")).IsEqualTo("Deutsch");
    }

    [Test]
    public async Task SetLanguage_to_polish_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("pl");

        await Assert.That(service.Language).IsEqualTo("pl");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Pulpit");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Zainstaluj");
        await Assert.That(service.GetString("Settings.Language.Pl")).IsEqualTo("Polski");
    }

    [Test]
    public async Task Available_languages_include_all_four_supported_languages()
    {
        var service = new LocalizationService("en");

        await Assert.That(service.AvailableLanguages).Contains("en");
        await Assert.That(service.AvailableLanguages).Contains("sk");
        await Assert.That(service.AvailableLanguages).Contains("de");
        await Assert.That(service.AvailableLanguages).Contains("pl");
    }

    [Test]
    public async Task Unsupported_language_still_falls_back_to_english()
    {
        var service = new LocalizationService("fr");

        await Assert.That(service.Language).IsEqualTo("en");
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
