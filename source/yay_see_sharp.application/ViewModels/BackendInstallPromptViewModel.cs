using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Shown once at startup when the host is Arch/CachyOS but `yay` wasn't found on PATH
/// (<see cref="BackendMode.Unavailable"/>). Confirming runs the real, previewed install command
/// through <see cref="IBackendInstaller"/>; a successful install still requires restarting the app
/// to actually switch backends (the running <see cref="MainWindowViewModel"/> tree was already
/// built against the Demo-backed fallback), which <see cref="RestartHintLabel"/> explains.
/// </summary>
public sealed class BackendInstallPromptViewModel : LocalizedViewModelBase
{
    private readonly IBackendInstaller _installer;
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private OperationViewModel? _operation;

    public BackendInstallPromptViewModel(IBackendInstaller installer, ILocalizationService localization)
        : base(localization)
    {
        _installer = installer;

        var notStarted = this.WhenAnyValue(x => x.Operation).Select(operation => operation is null);
        var notRunning = this.WhenAnyValue(x => x.Operation)
            .Select(operation => operation is null
                ? Observable.Return(true)
                : operation.WhenAnyValue(o => o.IsRunning).Select(running => !running))
            .Switch();

        ConfirmCommand = ReactiveCommand.CreateFromTask(ConfirmAsync, notStarted);
        CloseCommand = ReactiveCommand.Create(Close, notRunning);
    }

    public string DisplayCommand => _installer.DisplayCommand;

    public OperationViewModel? Operation
    {
        get => _operation;
        private set => this.RaiseAndSetIfChanged(ref _operation, value);
    }

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public string TitleLabel => Localization.GetString("BackendInstall.Title");

    public string BodyLabel => Localization.GetString("BackendInstall.Body");

    public string ConfirmLabel => Localization.GetString("BackendInstall.Confirm");

    public string CloseLabel => Localization.GetString("BackendInstall.Close");

    public string RestartHintLabel => Localization.GetString("BackendInstall.RestartHint");

    public bool ShowRestartHint => Operation?.Stage == PackageOperationStage.Completed;

    public Task WaitForResultAsync() => _completionSource.Task;

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(TitleLabel));
        this.RaisePropertyChanged(nameof(BodyLabel));
        this.RaisePropertyChanged(nameof(ConfirmLabel));
        this.RaisePropertyChanged(nameof(CloseLabel));
        this.RaisePropertyChanged(nameof(RestartHintLabel));
    }

    private async Task ConfirmAsync()
    {
        var operation = new OperationViewModel(PackageOperationKind.InstallBackend, Localization);
        operation.WhenAnyValue(o => o.Stage).Subscribe(_ => this.RaisePropertyChanged(nameof(ShowRestartHint)));
        Operation = operation;

        await foreach (var progress in _installer.InstallAsync(operation.CancellationToken))
        {
            operation.Apply(progress);
        }
    }

    private void Close() => _completionSource.TrySetResult();
}
