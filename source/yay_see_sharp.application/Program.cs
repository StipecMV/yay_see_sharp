using Avalonia;
using Avalonia.ReactiveUI;
using yay_see_sharp.application.Logging;

namespace yay_see_sharp.application
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Logging must be configured before anything else so every subsystem (composition
            // root, backend, command runner, ...) can rely on log4net being ready.
            LoggingSetup.Configure();
            LoggingSetup.LogProcessStart();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}
