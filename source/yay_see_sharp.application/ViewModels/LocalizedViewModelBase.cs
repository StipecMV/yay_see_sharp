using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

public abstract class LocalizedViewModelBase : ViewModelBase, IDisposable
{
    protected LocalizedViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    protected ILocalizationService Localization { get; }

    protected abstract void RaiseLocalizedPropertiesChanged();

    private void OnLanguageChanged(object? sender, EventArgs e) => RaiseLocalizedPropertiesChanged();

    public virtual void Dispose() => Localization.LanguageChanged -= OnLanguageChanged;
}
