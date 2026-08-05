namespace yay_see_sharp.domain.Abstractions;

public interface ITrayService : IDisposable
{
    void Show();

    void Hide();

    event EventHandler? RestoreRequested;

    event EventHandler? ExitRequested;
}
