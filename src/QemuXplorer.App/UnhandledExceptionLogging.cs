using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace QemuXplorer.App;

/// <summary>
/// Writes exceptions nothing caught to the log file. Without it a crash leaves nothing behind but
/// the operating system's dialog, because the app has no console and the log only held what the
/// code chose to log.
/// </summary>
/// <remarks>
/// It only records. It does not mark anything handled, so the process ends exactly as it did
/// before: a fatal error or an exception on the UI thread still terminates the app. What changes
/// is that the cause is in the file first. Add <c>e.Handled = true</c> in the UI-thread hook if the
/// app should instead stay alive after an error (it would then also want to tell the user).
/// </remarks>
public sealed class UnhandledExceptionLogging
{
    private readonly Func<ILogger?> _getLogger;
    private readonly string _fallbackFile;

    /// <param name="getLogger">
    /// Resolved on every report, not once, because the first reports can come before the
    /// dependency-injection container exists and the last after it has been disposed.
    /// </param>
    /// <param name="fallbackDirectory">
    /// Where <c>unhandled.log</c> goes when there is no logger, or when logging itself fails.
    /// </param>
    public UnhandledExceptionLogging(Func<ILogger?> getLogger, string fallbackDirectory)
    {
        _getLogger = getLogger;
        _fallbackFile = Path.Combine(fallbackDirectory, "unhandled.log");
    }

    /// <summary>Hooks the process-wide events. Call as early as possible.</summary>
    public void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Uninstall()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    /// <summary>Hooks the Avalonia UI thread. Needs Avalonia to be set up, so it is separate.</summary>
    public void InstallUiThreadHook()
        => Dispatcher.UIThread.UnhandledException += (_, e) => ReportUiThread(e.Exception);

    /// <summary>An exception that reached the top of any thread. When terminating, the process is going down.</summary>
    public void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ?? new InvalidOperationException(e.ExceptionObject?.ToString());
        Report(e.IsTerminating ? LogLevel.Critical : LogLevel.Error, "a thread", exception, e.IsTerminating);
    }

    /// <summary>A faulted Task that nothing awaited. It no longer crashes the process, but it is a lost error.</summary>
    public void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report(LogLevel.Error, "a task nobody awaited", e.Exception, terminating: false);
        e.SetObserved();
    }

    /// <summary>An exception thrown from an event handler or an async void method on the UI thread.</summary>
    public void ReportUiThread(Exception exception)
        => Report(LogLevel.Critical, "the UI thread", exception, terminating: true);

    private void Report(LogLevel level, string source, Exception exception, bool terminating)
    {
        var message = $"Unhandled exception on {source}" + (terminating ? "; the application is terminating" : string.Empty);

        try
        {
            var logger = _getLogger();
            if (logger is not null)
            {
                logger.Log(level, exception, "{Message}", message);
                return;
            }
        }
        catch (Exception)
        {
            // A failing logger must not hide the original error: fall through to the file.
        }

        WriteFallback(level, message, exception);
    }

    private void WriteFallback(LogLevel level, string message, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fallbackFile)!);
            File.AppendAllText(_fallbackFile,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nowhere left to write. A crash handler that throws would only make things worse.
        }
    }
}
