using Avalonia.Controls;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        /// <summary>
        /// UI-14: the main sidebar's SelectedItem binds one-way (VM -> control) — its ItemsSource
        /// is only a subset (MainNavigationItems) of every value SelectedNavigationItem can hold
        /// (it can also be the Settings item, shown in a separate button below). A two-way binding
        /// here would fight back: when the VM sets SelectedNavigationItem to Settings, the ListBox
        /// can't find it in its own Items and "corrects" SelectedItem back to null/its last valid
        /// value — and with TwoWay, that correction propagates back up and stomps the VM's Settings
        /// selection right after it was set. Handling the click side explicitly here avoids that
        /// round-trip entirely.
        /// </summary>
        private void OnMainSidebarSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ViewModel is { } viewModel && sender is ListBox { SelectedItem: NavigationItemViewModel item })
            {
                viewModel.SelectedNavigationItem = item;
            }
        }
    }
}
