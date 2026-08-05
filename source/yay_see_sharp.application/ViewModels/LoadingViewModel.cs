using ReactiveUI;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

public sealed class LoadingViewModel : LocalizedViewModelBase
{
    private int _percent;

    public LoadingViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public int Percent
    {
        get => _percent;
        set => this.RaiseAndSetIfChanged(ref _percent, value);
    }

    public string StatusLabel => Localization.GetString("Loading.Status");

    protected override void RaiseLocalizedPropertiesChanged() => this.RaisePropertyChanged(nameof(StatusLabel));
}
