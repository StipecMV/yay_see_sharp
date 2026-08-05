using ReactiveUI;

namespace yay_see_sharp.application.ViewModels;

public sealed class FolderEntryViewModel : ReactiveObject
{
    private bool _isSelected;

    public FolderEntryViewModel(string name, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}
