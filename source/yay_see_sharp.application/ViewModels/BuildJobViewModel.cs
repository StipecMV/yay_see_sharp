using System.Linq;
using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Presents an in-flight <see cref="OperationViewModel"/> as a build job: a package name plus a
/// minimize/restore flag so the build progress modal can be dismissed without cancelling the
/// underlying operation, which keeps running regardless of whether the modal is shown.
/// </summary>
public sealed class BuildJobViewModel : LocalizedViewModelBase
{
    private bool _isMinimized;

    public BuildJobViewModel(string packageName, OperationViewModel operation, ILocalizationService localization)
        : base(localization)
    {
        PackageName = packageName;
        Operation = operation;
        MinimizeCommand = ReactiveCommand.Create(() => { IsMinimized = true; });
        RestoreCommand = ReactiveCommand.Create(() => { IsMinimized = false; });
        Operation.WhenAnyValue(x => x.OutputText).Subscribe(_ => this.RaisePropertyChanged(nameof(LogLines)));
    }

    public string PackageName { get; }

    public OperationViewModel Operation { get; }

    public ReactiveCommand<Unit, Unit> MinimizeCommand { get; }

    public ReactiveCommand<Unit, Unit> RestoreCommand { get; }

    public bool IsMinimized
    {
        get => _isMinimized;
        set => this.RaiseAndSetIfChanged(ref _isMinimized, value);
    }

    public string TitleLabel => string.Format(Localization.GetString("Build.Title"), PackageName);

    public string RunInBackgroundLabel => Localization.GetString("Build.RunInBackground");

    public IReadOnlyList<string> LogLines => Operation.OutputText
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.TrimEnd('\r'))
        .ToArray();

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(TitleLabel));
        this.RaisePropertyChanged(nameof(RunInBackgroundLabel));
    }
}
