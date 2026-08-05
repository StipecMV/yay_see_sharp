using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Shown whenever an operation needs elevated privileges. Resolves the pending
/// <see cref="WaitForResultAsync"/> task with the entered password, or null if cancelled.
/// </summary>
public sealed class AuthPromptViewModel : LocalizedViewModelBase
{
    private readonly TaskCompletionSource<string?> _completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string _password = string.Empty;

    public AuthPromptViewModel(ILocalizationService localization)
        : base(localization)
    {
        var canAuthenticate = this.WhenAnyValue(x => x.Password).Select(password => !string.IsNullOrEmpty(password));
        CancelCommand = ReactiveCommand.Create(() => Resolve(null));
        AuthenticateCommand = ReactiveCommand.Create(() => Resolve(Password), canAuthenticate);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> AuthenticateCommand { get; }

    public string TitleLabel => Localization.GetString("AuthPrompt.Title");

    public string BodyLabel => Localization.GetString("AuthPrompt.Body");

    public string PasswordLabel => Localization.GetString("AuthPrompt.Password");

    public string CancelLabel => Localization.GetString("AuthPrompt.Cancel");

    public string AuthenticateLabel => Localization.GetString("AuthPrompt.Authenticate");

    public Task<string?> WaitForResultAsync() => _completionSource.Task;

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(TitleLabel));
        this.RaisePropertyChanged(nameof(BodyLabel));
        this.RaisePropertyChanged(nameof(PasswordLabel));
        this.RaisePropertyChanged(nameof(CancelLabel));
        this.RaisePropertyChanged(nameof(AuthenticateLabel));
    }

    private void Resolve(string? password) => _completionSource.TrySetResult(password);
}
