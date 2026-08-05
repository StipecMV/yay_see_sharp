using ReactiveUI;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

public sealed class NavigationItemViewModel : LocalizedViewModelBase
{
    private readonly string _titleKey;

    public NavigationItemViewModel(NavigationSection section, string titleKey, ILocalizationService localization)
        : base(localization)
    {
        Section = section;
        _titleKey = titleKey;
    }

    public NavigationSection Section { get; }

    public string Title => Localization.GetString(_titleKey);

    protected override void RaiseLocalizedPropertiesChanged() => this.RaisePropertyChanged(nameof(Title));
}
