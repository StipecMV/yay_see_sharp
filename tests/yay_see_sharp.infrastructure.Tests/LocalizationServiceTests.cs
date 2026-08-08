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
    public async Task Available_languages_include_all_supported_languages()
    {
        var service = new LocalizationService("en");

        await Assert.That(service.AvailableLanguages).Contains("en");
        await Assert.That(service.AvailableLanguages).Contains("sk");
        await Assert.That(service.AvailableLanguages).Contains("de");
        await Assert.That(service.AvailableLanguages).Contains("pl");
        await Assert.That(service.AvailableLanguages).Contains("ru");
        await Assert.That(service.AvailableLanguages).Contains("es");
        await Assert.That(service.AvailableLanguages).Contains("pt");
        await Assert.That(service.AvailableLanguages).Contains("it");
        await Assert.That(service.AvailableLanguages).Contains("zh-cn");
        await Assert.That(service.AvailableLanguages).Contains("zh-tw");
        await Assert.That(service.AvailableLanguages).Contains("ja");
    }

    [Test]
    public async Task SetLanguage_to_russian_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("ru");

        await Assert.That(service.Language).IsEqualTo("ru");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Обзор");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Установить");
        await Assert.That(service.GetString("Settings.Language.Ru")).IsEqualTo("Русский");
    }

    [Test]
    public async Task SetLanguage_to_spanish_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("es");

        await Assert.That(service.Language).IsEqualTo("es");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Panel");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Instalar");
        await Assert.That(service.GetString("Settings.Language.Es")).IsEqualTo("Español");
    }

    [Test]
    public async Task SetLanguage_to_portuguese_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("pt");

        await Assert.That(service.Language).IsEqualTo("pt");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Painel");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Instalar");
        await Assert.That(service.GetString("Settings.Language.Pt")).IsEqualTo("Português");
    }

    [Test]
    public async Task SetLanguage_to_italian_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("it");

        await Assert.That(service.Language).IsEqualTo("it");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("Pannello");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("Installa");
        await Assert.That(service.GetString("Settings.Language.It")).IsEqualTo("Italiano");
    }

    [Test]
    public async Task SetLanguage_to_simplified_chinese_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("zh-CN");

        await Assert.That(service.Language).IsEqualTo("zh-cn");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("概览");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("安装");
        await Assert.That(service.GetString("Settings.Language.Zh-cn")).IsEqualTo("简体中文");
    }

    [Test]
    public async Task SetLanguage_to_traditional_chinese_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("zh-TW");

        await Assert.That(service.Language).IsEqualTo("zh-tw");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("總覽");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("安裝");
        await Assert.That(service.GetString("Settings.Language.Zh-tw")).IsEqualTo("繁體中文");
    }

    [Test]
    public async Task SetLanguage_to_japanese_switches_translated_strings()
    {
        var service = new LocalizationService("en");

        service.SetLanguage("ja");

        await Assert.That(service.Language).IsEqualTo("ja");
        await Assert.That(service.GetString("Navigation.Dashboard")).IsEqualTo("ダッシュボード");
        await Assert.That(service.GetString("Package.Install")).IsEqualTo("インストール");
        await Assert.That(service.GetString("Settings.Language.Ja")).IsEqualTo("日本語");
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
