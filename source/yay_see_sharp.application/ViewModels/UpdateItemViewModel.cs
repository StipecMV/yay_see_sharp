using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public sealed class UpdateItemViewModel : LocalizedViewModelBase
{
    public UpdateItemViewModel(UpdateInfo info, ReactiveCommand<string, Unit> updateCommand, ILocalizationService localization)
        : base(localization)
    {
        Info = info;
        UpdateCommand = updateCommand;
    }

    public UpdateInfo Info { get; }

    public ReactiveCommand<string, Unit> UpdateCommand { get; }

    public string UpdateLabel => Localization.GetString("Dashboard.UpdatePackage");

    protected override void RaiseLocalizedPropertiesChanged() => this.RaisePropertyChanged(nameof(UpdateLabel));
}
