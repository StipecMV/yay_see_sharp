using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace yay_see_sharp.e2e.Tests;

public class AuthPromptE2ETests
{
    [Test]
    public async Task Pressing_enter_in_the_password_field_confirms_the_same_way_as_clicking_authenticate()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            var resultTask = viewModel.RequestAuthenticationAsync();
            AvaloniaUiTest.Pump();

            await Assert.That(viewModel.AuthPrompt).IsNotNull();
            viewModel.AuthPrompt!.Password = "hunter2";
            AvaloniaUiTest.Pump();

            var passwordBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => ReferenceEquals(box.DataContext, viewModel.AuthPrompt));
            passwordBox.Focus();
            AvaloniaUiTest.Pump();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            AvaloniaUiTest.Pump();

            var result = await resultTask;

            await Assert.That(result).IsEqualTo("hunter2");
            await Assert.That(viewModel.AuthPrompt).IsNull();
        });
    }

    [Test]
    public async Task Pressing_enter_with_an_empty_password_does_not_confirm()
    {
        await AvaloniaUiTest.RunAsync(async () =>
        {
            var (window, viewModel, _) = TestShellFactory.Create();
            await viewModel.Dashboard.InitialLoadTask;
            AvaloniaUiTest.Pump();

            _ = viewModel.RequestAuthenticationAsync();
            AvaloniaUiTest.Pump();

            var passwordBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => ReferenceEquals(box.DataContext, viewModel.AuthPrompt));
            passwordBox.Focus();
            AvaloniaUiTest.Pump();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            AvaloniaUiTest.Pump();

            // Empty password never satisfies AuthenticateCommand's CanExecute, so the prompt must
            // still be showing — Enter is not a bypass for the empty-password guard.
            await Assert.That(viewModel.AuthPrompt).IsNotNull();

            viewModel.AuthPrompt!.CancelCommand.Execute().Subscribe();
        });
    }
}
