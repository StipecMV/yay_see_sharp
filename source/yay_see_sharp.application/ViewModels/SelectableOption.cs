using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace yay_see_sharp.application.ViewModels;

public sealed class SelectableOption<T> : INotifyPropertyChanged
{
    private string _label;

    public SelectableOption(T value, string label)
    {
        Value = value;
        _label = label;
    }

    public T Value { get; }

    public string Label
    {
        get => _label;
        set
        {
            if (_label == value) return;
            _label = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
