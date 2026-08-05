namespace yay_see_sharp.domain.Abstractions;

public interface ILocalizationService
{
    string Language { get; }

    IReadOnlyList<string> AvailableLanguages { get; }

    event EventHandler? LanguageChanged;

    void SetLanguage(string language);

    string GetString(string key);
}
