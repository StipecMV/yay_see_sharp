using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.ViewModels;

/// <summary>UI-22: a single in-app toast notification. Immutable content — only <see cref="DismissCommand"/> ever changes anything, and that just asks <see cref="ToastService"/> to remove it from the visible stack.</summary>
public sealed class ToastViewModel : ViewModelBase
{
    public ToastViewModel(string title, string body, NotificationLevel level, Action<ToastViewModel> onDismiss)
    {
        Title = title;
        Body = body;
        Level = level;
        DismissCommand = ReactiveCommand.Create(() => onDismiss(this));
    }

    public string Title { get; }

    public string Body { get; }

    public NotificationLevel Level { get; }

    public ReactiveCommand<Unit, Unit> DismissCommand { get; }
}
