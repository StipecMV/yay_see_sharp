using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

        private void OnBreadcrumbPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control { Tag: string path } && ViewModel?.FolderBrowser is { } browser)
            {
                browser.NavigateCommand.Execute(path).Subscribe();
            }
        }

        private void OnFolderEntryPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control { Tag: FolderEntryViewModel entry } && ViewModel?.FolderBrowser is { } browser)
            {
                browser.SelectEntryCommand.Execute(entry).Subscribe();
            }
        }

        private void OnFolderEntryDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control { Tag: FolderEntryViewModel entry } && ViewModel?.FolderBrowser is { } browser)
            {
                browser.OpenEntryCommand.Execute(entry).Subscribe();
            }
        }
    }
}
