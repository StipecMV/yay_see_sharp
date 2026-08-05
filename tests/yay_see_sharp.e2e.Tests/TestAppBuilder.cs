using Avalonia;
using Avalonia.Headless;
using Avalonia.ReactiveUI;
using yay_see_sharp.application;

[assembly: AvaloniaTestApplication(typeof(yay_see_sharp.e2e.Tests.TestAppBuilder))]

namespace yay_see_sharp.e2e.Tests;

/// <summary>
/// Entry point HeadlessUnitTestSession looks for (by convention, a static BuildAvaloniaApp()) to
/// boot a headless instance of the real App — so tests exercise the actual App.axaml styles/theme
/// merge and the real ViewLocator, not a stand-in. App.OnFrameworkInitializationCompleted (which
/// does real I/O — settings file, sudo, notify-send) is never invoked in headless tests: the
/// session only calls Initialize()/SetupWithoutStarting(), never the desktop lifetime that would
/// trigger it. Tests build their own MainWindow/View + ViewModel graph instead.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseReactiveUI()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
