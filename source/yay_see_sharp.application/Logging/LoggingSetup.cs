using System.Reflection;
using System.Text;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;

namespace yay_see_sharp.application.Logging;

/// <summary>
/// One-time log4net configuration for the whole application, done in Program.Main before
/// anything else runs. Logs land in <c>~/.config/yay_see_sharp/logs/</c> (same app-data root as
/// settings.json).
///
/// Rotation, per user request (2026-08): every app run gets a NEW log file named
/// <c>yay-see-sharp-&lt;start&gt;-&lt;pid&gt;.log</c>; when the active file reaches 10 MB it rolls —
/// the full segment becomes <c>.1</c> and a fresh one starts. With exactly one size backup, a
/// run therefore never holds more than two files (current + previous segment); when a third
/// segment would start, the oldest backup is deleted first. Previous runs' files are left in
/// place untouched.
/// </summary>
public static class LoggingSetup
{
    private const string LogFilePattern = "yay-see-sharp-{0:yyyyMMdd-HHmmss}-{1}.log";
    private const long MaxSegmentBytes = 10 * 1024 * 1024;

    /// <summary>Directory all app runs write their log segments into.</summary>
    public static string GetLogDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "yay_see_sharp",
        "logs");

    /// <summary>Configures the root log4net repository: INFO level, rolling file appender with
    /// the per-run name above. Safe to call once; a second call is a no-op.</summary>
    public static void Configure()
    {
        var repository = LogManager.GetRepository() as Hierarchy;
        if (repository is null || repository.Configured)
        {
            return;
        }

        var directory = GetLogDirectory();
        Directory.CreateDirectory(directory);

        var appender = new RollingFileAppender
        {
            File = Path.Combine(directory, string.Format(LogFilePattern, DateTime.Now, Environment.ProcessId)),
            AppendToFile = false,
            RollingStyle = RollingFileAppender.RollingMode.Size,
            MaxFileSize = MaxSegmentBytes,
            MaxSizeRollBackups = 1,
            StaticLogFileName = true,
            ImmediateFlush = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Layout = new PatternLayout("%date{yyyy-MM-dd HH:mm:ss.fff} [%thread] %-5level %logger{2} - %message%newline%exception"),
        };
        appender.ActivateOptions();

        repository.Root.AddAppender(appender);
        repository.Root.Level = Level.Info;
        repository.Configured = true;

        // Route Splat's logger (used by ReactiveUI internals — bindings, command execution,
        // scheduler warnings) into the same file. Best-effort: if Splat's resolver isn't ready
        // yet this must never take the app down.
        try
        {
            Splat.Locator.CurrentMutable.Register(
                () => new Splat.Log4Net.Log4NetLogger(LogManager.GetLogger("Splat")),
                typeof(Splat.ILogger));
        }
        catch (Exception exception)
        {
            LogManager.GetLogger(typeof(LoggingSetup))
                .Warn("Could not route Splat logging to log4net.", exception);
        }
    }

    /// <summary>Logs the process identity line (version, OS, PID) every run starts with.</summary>
    public static void LogProcessStart()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var logger = LogManager.GetLogger(typeof(LoggingSetup));
        logger.Info($"yay-see-sharp starting — version {version}, OS {Environment.OSVersion}, PID {Environment.ProcessId}");
        logger.Info($"Log file directory: {GetLogDirectory()}");
    }
}
