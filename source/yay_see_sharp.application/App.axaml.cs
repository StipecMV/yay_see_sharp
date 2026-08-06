using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using yay_see_sharp.application.Platform;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.application.Views;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Scheduling;

namespace yay_see_sharp.application
{
    public partial class App : Application
    {
        private ISingleInstanceService? _singleInstanceService;
        private AvaloniaTrayService? _trayService;
        private UpdateScheduler? _updateScheduler;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var composition = AppBootstrapper.Bootstrap();
                if (composition is null)
                {
                    desktop.Shutdown();
                    base.OnFrameworkInitializationCompleted();
                    return;
                }

                _singleInstanceService = composition.SingleInstanceService;
                _updateScheduler = composition.UpdateScheduler;
                _trayService = composition.TrayService;
                var viewModel = composition.ViewModel;

                _updateScheduler.StartAsync().FireAndForget();

                var window = new MainWindow { DataContext = viewModel };
                desktop.MainWindow = window;

                // UI-21: the tray icon is registered at startup, not only after the first
                // minimize/close — so it's there to restore from even if the user never triggers
                // a hide-to-tray in that session.
                _trayService.Show();

                // Fires on the single-instance service's socket-listener thread, never the UI
                // thread — every touch of `window` below must go through the dispatcher.
                _singleInstanceService.ActivationRequested += (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                    window.Activate();
                });

                viewModel.Settings.WhenAnyValue(x => x.Theme).Subscribe(theme =>
                {
                    RequestedThemeVariant = theme switch
                    {
                        ThemePreference.Light => ThemeVariant.Light,
                        ThemePreference.Dark => ThemeVariant.Dark,
                        _ => ThemeVariant.Default,
                    };
                });

                _trayService.RestoreRequested += (_, _) =>
                {
                    window.Show();
                    window.WindowState = WindowState.Normal;
                };
                _trayService.ExitRequested += (_, _) => desktop.Shutdown();

                desktop.Exit += (_, _) => _updateScheduler.StopAsync().FireAndForget();

                window.Closing += (_, args) =>
                {
                    if (viewModel.Settings.CloseAction == CloseAction.HideToTray)
                    {
                        args.Cancel = true;
                        window.Hide();
                    }
                };

                // UI-02: minimizing behaves the same as closing (X) when hide-to-tray is enabled —
                // the window disappears into the tray instead of sitting minimized in the taskbar.
                window.GetObservable(Window.WindowStateProperty).Subscribe(state =>
                {
                    if (state == WindowState.Minimized && viewModel.Settings.CloseAction == CloseAction.HideToTray)
                    {
                        window.Hide();
                    }
                });
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
