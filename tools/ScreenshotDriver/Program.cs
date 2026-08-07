using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.VisualTree;
using yay_see_sharp.application;
using yay_see_sharp.application.ViewModels;
using yay_see_sharp.application.Views;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Http;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Platform;
using yay_see_sharp.infrastructure.Settings;

namespace ScreenshotDriver;

/// <summary>
/// Regenerates the README screenshots (docs/screenshots/*.png) against the current UI.
/// Runs the real compiled app on an X11 display (Xvfb works; a regular desktop session works too)
/// with the Demo backend, navigates every screen, and saves each frame as PNG.
///
/// Usage (from the repository root):
///   ./tools/generate-screenshots.sh                 # starts Xvfb if needed, then runs this
///   dotnet run --project tools/ScreenshotDriver     # requires DISPLAY to be set
///
/// Options:
///   --out &lt;dir&gt;   output directory (default: &lt;repo&gt;/docs/screenshots)
///   --size WxH     window size, e.g. 1280x800 (default)
///   --theme L|D|S  theme override: Light, Dark, System (default: System)
///   --lang en|sk   UI language (default: en)
/// </summary>
public static class Program
{
    private sealed class DispatcherSyncContext : SynchronizationContext
    {
        private readonly Dispatcher _dispatcher;

        public DispatcherSyncContext(Dispatcher dispatcher) => _dispatcher = dispatcher;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _dispatcher.Post(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state) =>
            _dispatcher.Invoke(() => callback(state));
    }

    private sealed class Options
    {
        public string OutputDir = ResolveDefaultOutputDir();
        public int Width = 1280;
        public int Height = 800;
        public ThemePreference Theme = ThemePreference.System;
        public string Language = "en";
    }

    public static int Main(string[] args)
    {
        var options = Parse(args);

        var builder = AppBuilder.Configure<App>()
            .UseReactiveUI()
            .UsePlatformDetect();
        builder.SetupWithoutStarting();

        SynchronizationContext.SetSynchronizationContext(new DispatcherSyncContext(Dispatcher.UIThread));

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                tcs.SetResult(await RunAsync(options));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAILED: {ex}");
                tcs.SetResult(1);
            }
        });

        while (!tcs.Task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        return tcs.Task.Result;
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    options.OutputDir = Path.GetFullPath(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                {
                    var parts = args[++i].Split('x');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out var width) &&
                        int.TryParse(parts[1], out var height) &&
                        width > 0 && height > 0)
                    {
                        options.Width = width;
                        options.Height = height;
                    }
                    else
                    {
                        Console.Error.WriteLine($"Invalid --size '{args[i]}' (expected e.g. 1280x800); using default.");
                    }

                    break;
                }
                case "--theme" when i + 1 < args.Length:
                {
                    var value = args[++i].ToLowerInvariant();
                    options.Theme = value switch
                    {
                        "light" or "l" => ThemePreference.Light,
                        "dark" or "d" => ThemePreference.Dark,
                        _ => ThemePreference.System,
                    };
                    break;
                }
                case "--lang" when i + 1 < args.Length:
                {
                    var value = args[++i].ToLowerInvariant();
                    if (value is "en" or "sk")
                    {
                        options.Language = value;
                    }

                    break;
                }
                default:
                    Console.Error.WriteLine($"Unknown argument '{args[i]}' (ignored).");
                    break;
            }
        }

        return options;
    }

    private static async Task<int> RunAsync(Options options)
    {
        var backend = new DemoPackageBackend();
        var localization = new LocalizationService(options.Language);
        var settingsStore = new FileSettingsStore(
            Path.Combine(Path.GetTempPath(), $"yay-see-sharp-screenshot-{Guid.NewGuid():N}.json"));
        var settings = new SettingsViewModel(settingsStore, localization, AppSettings.Default, new EngineDetector())
        {
            Theme = options.Theme,
        };
        var pkgbuildService = new PkgbuildService();
        var dashboard = new DashboardViewModel(backend, localization);
        var search = new SearchViewModel(backend, localization, pkgbuildService, settings);
        var installed = new InstalledPackagesViewModel(backend, localization, pkgbuildService, settings);
        var viewModel = new MainWindowViewModel(backend, localization, settings, dashboard, search, installed);

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = options.Width,
            Height = options.Height,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Directory.CreateDirectory(options.OutputDir);

        await dashboard.InitialLoadTask;
        Dispatcher.UIThread.RunJobs();
        Capture(window, options.OutputDir, "dashboard.png");

        var sidebar = window.GetVisualDescendants().OfType<ListBox>()
            .First(box => ReferenceEquals(box.ItemsSource, viewModel.MainNavigationItems));

        sidebar.SelectedItem = viewModel.NavigationItems[1];
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => search.Results.Count > 0, TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();
        Capture(window, options.OutputDir, "search.png");

        search.SelectedPackage = search.Results[0];
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => search.SelectedDetails is not null, TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();
        Capture(window, options.OutputDir, "package-detail.png");

        sidebar.SelectedItem = viewModel.NavigationItems[2];
        Dispatcher.UIThread.RunJobs();
        await WaitUntilAsync(() => installed.Packages.Count > 0, TimeSpan.FromSeconds(15));
        Dispatcher.UIThread.RunJobs();
        Capture(window, options.OutputDir, "installed.png");

        await viewModel.SelectSettingsCommand.Execute();
        Dispatcher.UIThread.RunJobs();
        Capture(window, options.OutputDir, "settings.png");

        window.Close();
        Console.WriteLine($"Screenshots written to {options.OutputDir}");
        return 0;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within the timeout.");
        }
    }

    private static void Capture(MainWindow window, string outputDir, string fileName)
    {
        var size = window.ClientSize;
        var width = (int)(size.Width > 0 ? size.Width : 1280);
        var height = (int)(size.Height > 0 ? size.Height : 800);

        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(window);

        var path = Path.Combine(outputDir, fileName);
        bitmap.Save(path, new PngBitmapEncoderOptions());
        Console.WriteLine($"Saved {path} ({new FileInfo(path).Length} bytes)");
    }

    private static string ResolveDefaultOutputDir()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "screenshots");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("docs/screenshots not found — run from the repository root.");
    }
}
