using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

public class AuthPromptViewModelTests
{
    [Test]
    public async Task Authenticate_command_is_disabled_when_password_is_empty()
    {
        var viewModel = new AuthPromptViewModel(new LocalizationService("en"));

        await Assert.That(((ICommand)viewModel.AuthenticateCommand).CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Authenticate_command_becomes_enabled_once_a_password_is_entered()
    {
        var viewModel = new AuthPromptViewModel(new LocalizationService("en"));

        viewModel.Password = "hunter2";

        await Assert.That(((ICommand)viewModel.AuthenticateCommand).CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Authenticate_command_disables_again_when_password_is_cleared()
    {
        var viewModel = new AuthPromptViewModel(new LocalizationService("en"));
        viewModel.Password = "hunter2";

        viewModel.Password = string.Empty;

        await Assert.That(((ICommand)viewModel.AuthenticateCommand).CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Authenticate_command_resolves_the_pending_result_with_the_entered_password()
    {
        var viewModel = new AuthPromptViewModel(new LocalizationService("en"));
        viewModel.Password = "hunter2";

        var resultTask = viewModel.WaitForResultAsync();
        await viewModel.AuthenticateCommand.Execute();

        await Assert.That(await resultTask).IsEqualTo("hunter2");
    }

    [Test]
    public async Task Cancel_command_resolves_the_pending_result_with_null()
    {
        var viewModel = new AuthPromptViewModel(new LocalizationService("en"));
        viewModel.Password = "hunter2";

        var resultTask = viewModel.WaitForResultAsync();
        await viewModel.CancelCommand.Execute();

        await Assert.That(await resultTask).IsNull();
    }

    [Test]
    public async Task Switching_language_live_updates_prompt_labels()
    {
        var localization = new LocalizationService("en");
        var viewModel = new AuthPromptViewModel(localization);

        await Assert.That(viewModel.TitleLabel).IsEqualTo("Authentication required");
        await Assert.That(viewModel.AuthenticateLabel).IsEqualTo("Authenticate");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.TitleLabel).IsEqualTo("Vyžaduje sa overenie");
        await Assert.That(viewModel.AuthenticateLabel).IsEqualTo("Overiť");
    }
}
