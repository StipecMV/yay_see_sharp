using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using ReactiveUI;
using yay_see_sharp.application.Helpers;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.application.Views;

namespace yay_see_sharp.application
{
    public partial class App : Application
    {
        private MainWindow? myMainWindow;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                myMainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
                desktop.MainWindow = myMainWindow;

                RegisterTrayIcon();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void MyMainWindow_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (sender is MainWindow && e.NewValue is WindowState windowState && windowState == WindowState.Minimized)
            {
                myMainWindow?.Hide();
            }
        }

        private void RegisterTrayIcon()
        {
            var trayIcon = new TrayIcon
            {
                IsVisible = true,
                ToolTipText = Constants.GetApplicationName(),
                Command = ReactiveCommand.Create(ShowApplication),
                Icon = new WindowIcon(new Bitmap($"{Constants.GetAssetsDirectory()}/test.png"))
            };

            var trayIcons = new TrayIcons
            {
                trayIcon
            };

            SetValue(TrayIcon.IconsProperty, trayIcons);
        }

        private void ShowApplication()
        {
            if(myMainWindow != null)
            {
                myMainWindow.WindowState = WindowState.Normal;
                myMainWindow.Show();
            }
        }
    }
}