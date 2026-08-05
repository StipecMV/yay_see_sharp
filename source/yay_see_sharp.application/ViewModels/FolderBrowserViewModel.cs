using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Filesystem;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Custom in-app folder browser used by Settings' "Browse…" build-directory field, so the app
/// never has to shell out to the native GTK/Qt file picker (which would break visual consistency
/// with the rest of the UI).
/// </summary>
public sealed class FolderBrowserViewModel : LocalizedViewModelBase
{
    private static readonly string HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly IFolderBrowserService _folderBrowserService;
    private readonly TaskCompletionSource<string?> _completionSource =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string _currentPath;
    private string _selectedPath;

    public FolderBrowserViewModel(string startPath, ILocalizationService localization, IFolderBrowserService? folderBrowserService = null)
        : base(localization)
    {
        _folderBrowserService = folderBrowserService ?? new FolderBrowserService();
        _currentPath = ResolveStartPath(startPath);
        _selectedPath = _currentPath;

        NavigateCommand = ReactiveCommand.Create<string>(NavigateTo);
        SelectEntryCommand = ReactiveCommand.Create<FolderEntryViewModel>(entry => SelectedPath = entry.FullPath);
        OpenEntryCommand = ReactiveCommand.Create<FolderEntryViewModel>(entry => NavigateTo(entry.FullPath));
        SelectFolderCommand = ReactiveCommand.Create(() => Resolve(SelectedPath));
        CancelCommand = ReactiveCommand.Create(() => Resolve(null));

        Refresh();
    }

    public ObservableCollection<FolderEntryViewModel> Children { get; } = [];

    public ObservableCollection<BreadcrumbSegmentViewModel> Breadcrumbs { get; } = [];

    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    public ReactiveCommand<FolderEntryViewModel, Unit> SelectEntryCommand { get; }

    public ReactiveCommand<FolderEntryViewModel, Unit> OpenEntryCommand { get; }

    public ReactiveCommand<Unit, Unit> SelectFolderCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public string CurrentPath
    {
        get => _currentPath;
        private set => this.RaiseAndSetIfChanged(ref _currentPath, value);
    }

    public string SelectedPath
    {
        get => _selectedPath;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedPath, value);
            this.RaisePropertyChanged(nameof(DisplayPath));
            foreach (var entry in Children)
            {
                entry.IsSelected = entry.FullPath == value;
            }
        }
    }

    public string DisplayPath => Collapse(SelectedPath);

    public string TitleLabel => Localization.GetString("FolderBrowser.Title");

    public string CancelLabel => Localization.GetString("FolderBrowser.Cancel");

    public string SelectLabel => Localization.GetString("FolderBrowser.Select");

    public Task<string?> WaitForResultAsync() => _completionSource.Task;

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(TitleLabel));
        this.RaisePropertyChanged(nameof(CancelLabel));
        this.RaisePropertyChanged(nameof(SelectLabel));
    }

    private void NavigateTo(string path)
    {
        CurrentPath = path;
        SelectedPath = path;
        Refresh();
    }

    private void Refresh()
    {
        Breadcrumbs.Clear();
        var accumulated = System.IO.Path.GetPathRoot(CurrentPath) ?? "/";
        foreach (var part in CurrentPath
                     .Substring(accumulated.Length)
                     .Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            accumulated = System.IO.Path.Combine(accumulated, part);
            Breadcrumbs.Add(new BreadcrumbSegmentViewModel(part, accumulated));
        }

        Children.Clear();
        foreach (var directory in _folderBrowserService.GetSubdirectories(CurrentPath))
        {
            Children.Add(new FolderEntryViewModel(System.IO.Path.GetFileName(directory), directory) { IsSelected = directory == SelectedPath });
        }
    }

    private string ResolveStartPath(string startPath)
    {
        var expanded = Expand(startPath);
        var candidate = _folderBrowserService.DirectoryExists(expanded) ? expanded : HomeDirectory;
        return Path.TrimEndingDirectorySeparator(candidate);
    }

    private static string Expand(string path) => path.StartsWith('~')
        ? Path.Combine(HomeDirectory, path.TrimStart('~').TrimStart('/'))
        : path;

    private static string Collapse(string path) => path.StartsWith(HomeDirectory, StringComparison.Ordinal)
        ? "~" + path[HomeDirectory.Length..]
        : path;

    private void Resolve(string? path) => _completionSource.TrySetResult(path);
}
