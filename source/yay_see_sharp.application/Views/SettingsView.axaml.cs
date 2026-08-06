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

        private void OnEngineOptionsPressed(object? sender, PointerPressedEventArgs e) =>
            ViewModel?.NotifyIfSimulated();
    }
}
