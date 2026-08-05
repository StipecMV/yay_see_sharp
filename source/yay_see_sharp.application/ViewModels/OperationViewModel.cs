using System.Reactive;
using System.Text;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

public class OperationViewModel : LocalizedViewModelBase
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly StringBuilder _output = new();

    private PackageOperationStage _stage = PackageOperationStage.Preparing;
    private int _percent;
    private string _message = string.Empty;
    private string? _command;
    private string _outputText = string.Empty;
    private bool _isRunning = true;

    public OperationViewModel(PackageOperationKind kind, ILocalizationService localization)
        : base(localization)
    {
        Kind = kind;
        CancelCommand = ReactiveCommand.Create(Cancel, this.WhenAnyValue(x => x.IsRunning));
    }

    public PackageOperationKind Kind { get; }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public string CancelLabel => Localization.GetString("Operation.Cancel");

    public string StageLabel => Localization.GetString($"Operation.Stage.{Stage}");

    public PackageOperationStage Stage
    {
        get => _stage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _stage, value);
            this.RaisePropertyChanged(nameof(StageLabel));
        }
    }

    public int Percent
    {
        get => _percent;
        private set => this.RaiseAndSetIfChanged(ref _percent, value);
    }

    public string Message
    {
        get => _message;
        private set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public string? Command
    {
        get => _command;
        private set => this.RaiseAndSetIfChanged(ref _command, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => this.RaiseAndSetIfChanged(ref _outputText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(CancelLabel));
        this.RaisePropertyChanged(nameof(StageLabel));
    }

    public void Apply(PackageOperationProgress progress)
    {
        Stage = progress.Stage;
        Percent = progress.Percent;
        Message = progress.Message;

        if (progress.Command is not null)
        {
            Command = progress.Command;
        }

        if (progress.Output is not null)
        {
            _output.AppendLine(progress.Output);
            OutputText = _output.ToString();
        }

        IsRunning = progress.Stage is
            not PackageOperationStage.Completed and
            not PackageOperationStage.Failed and
            not PackageOperationStage.Cancelled;
    }

    private void Cancel() => _cancellationTokenSource.Cancel();
}
