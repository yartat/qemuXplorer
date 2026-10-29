using Microsoft.Extensions.Logging;
using QemuXplorer.Data;
using Serilog;
using Serilog.Events;

namespace QemuXplorer.App;

/// <summary>Where and how much the application logs.</summary>
/// <param name="Directory">Folder for the rolling log files.</param>
/// <param name="Level">Minimum level written.</param>
/// <param name="Console">Also write to the console. Off unless asked for.</param>
public sealed record LoggingOptions(string Directory, LogLevel Level, bool Console)
{
    public const string ConsoleVariable = "QEMUXPLORER_LOG_CONSOLE";
    public const string LevelVariable = "QEMUXPLORER_LOG_LEVEL";

    public static LoggingOptions Default { get; } = FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Logs go to a file under the data directory. The console is opt-in because a desktop app has
    /// no console to read, and writing there from a GUI process only costs time. Set
    /// <c>QEMUXPLORER_LOG_CONSOLE=1</c> to get it back when running from a terminal, and
    /// <c>QEMUXPLORER_LOG_LEVEL</c> (Trace, Debug, Information, Warning, Error, Critical) to change
    /// the level. An unrecognised value is ignored rather than treated as an error.
    /// </summary>
    public static LoggingOptions FromEnvironment(Func<string, string?> getVariable, string? directory = null)
    {
        var console = getVariable(ConsoleVariable) is { } c
                      && (c == "1" || c.Equals("true", StringComparison.OrdinalIgnoreCase) || c.Equals("yes", StringComparison.OrdinalIgnoreCase));

        var level = Enum.TryParse<LogLevel>(getVariable(LevelVariable), ignoreCase: true, out var parsed)
                    && Enum.IsDefined(parsed) && parsed != LogLevel.None
            ? parsed
            : LogLevel.Information;

        return new LoggingOptions(directory ?? Path.Combine(DbPathHelper.GetDataDirectory(), "logs"), level, console);
    }
}

public static class AppLogging
{
    private const string FileTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Replaces the default logging providers with a rolling file, and adds the console only when
    /// <see cref="LoggingOptions.Console"/> is set. Logging must never stop the app from starting,
    /// so an unwritable folder just means nothing is written.
    /// </summary>
    public static void Configure(ILoggingBuilder builder, LoggingOptions options)
    {
        builder.ClearProviders();

        // These two rules govern the console only. AddSerilog removes Microsoft.Extensions.Logging's
        // level rules for its own provider (it registers a Trace rule for it), so the file's level
        // and the EF Core cap are set inside Serilog, in CreateFileLogger.
        builder.SetMinimumLevel(options.Level);
        if (CapsEntityFramework(options))
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);

        builder.AddSerilog(CreateFileLogger(options), dispose: true);

        if (options.Console)
            builder.AddConsole();
    }

    // EF Core logs every SQL command and connection at Information. That would drown the file, so
    // it is capped at Warning unless the user asked for Debug or Trace to look at it.
    private static bool CapsEntityFramework(LoggingOptions options) => options.Level > LogLevel.Debug;

    private static LogEventLevel ToSerilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    private static Serilog.ILogger CreateFileLogger(LoggingOptions options)
    {
        var directory = options.Directory;
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Serilog reports the failed writes to its own self-log, not to the caller.
        }

        var configuration = new LoggerConfiguration().MinimumLevel.Is(ToSerilog(options.Level));
        if (CapsEntityFramework(options))
            configuration.MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);

        return configuration
            .WriteTo.File(
                Path.Combine(directory, "qemuxplorer-.log"),
                outputTemplate: FileTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                // Two running copies of the app must not lock each other out of the log.
                shared: true)
            .CreateLogger();
    }
}
