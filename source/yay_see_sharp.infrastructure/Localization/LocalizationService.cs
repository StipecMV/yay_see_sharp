using System.Globalization;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Resources;

namespace yay_see_sharp.infrastructure.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private string _language;

    public LocalizationService(string? language = null)
    {
        _language = Normalize(language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
    }

    public event EventHandler? LanguageChanged;

    public string Language => _language;

    public IReadOnlyList<string> AvailableLanguages => LocalizationResources.Strings.Keys.ToArray();

    public void SetLanguage(string language)
    {
        var normalized = Normalize(language);
        if (normalized == _language)
        {
            return;
        }

        _language = normalized;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        if (LocalizationResources.Strings[_language].TryGetValue(key, out var value))
        {
            return value;
        }

        return LocalizationResources.Strings[LocalizationResources.English].GetValueOrDefault(key, key);
    }

    private static string Normalize(string language)
    {
        var candidate = language.Trim().ToLowerInvariant();
        return LocalizationResources.Strings.ContainsKey(candidate)
            ? candidate
            : LocalizationResources.English;
    }
}
