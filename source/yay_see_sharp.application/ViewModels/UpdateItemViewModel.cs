using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public sealed class UpdateItemViewModel : LocalizedViewModelBase
{
    public UpdateItemViewModel(
        UpdateInfo info,
        ReactiveCommand<string, Unit> updateCommand,
        ReactiveCommand<string, Unit> selectCommand,
        ILocalizationService localization)
        : base(localization)
    {
        Info = info;
        UpdateCommand = updateCommand;
        SelectCommand = selectCommand;
    }

    public UpdateInfo Info { get; }

    public ReactiveCommand<string, Unit> UpdateCommand { get; }

    /// <summary>UI-05: clicking the row (not the Update button) navigates to this package's detail on the Installed screen.</summary>
    public ReactiveCommand<string, Unit> SelectCommand { get; }

    public string UpdateLabel => Localization.GetString("Dashboard.UpdatePackage");

    protected override void RaiseLocalizedPropertiesChanged() => this.RaisePropertyChanged(nameof(UpdateLabel));
}
