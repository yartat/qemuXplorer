using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QemuXplorer.App;
using Xunit;

namespace QemuXplorer.App.Tests;

public class UnhandledExceptionLoggingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-unhandled-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>The real file logger the app uses, pointed at a temporary folder.</summary>
    private ServiceProvider RealLogging()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => AppLogging.Configure(b, new LoggingOptions(_dir, LogLevel.Information, Console: false)));
        return services.BuildServiceProvider();
    }

    private string LogText() =>
        string.Concat(Directory.GetFiles(_dir, "qemuxplorer-*.log").Select(path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }));

    /// <summary>An exception that has actually been thrown, so it carries a stack trace.</summary>
    private static InvalidOperationException Thrown(string message)
    {
        try { throw new InvalidOperationException(message); }
        catch (InvalidOperationException ex) { return ex; }
    }

    [Fact]
    public void AFatalException_IsWrittenAsCriticalWithItsStackTrace_AndSaysTheAppIsTerminating()
    {
        string text;
        using (var provider = RealLogging())
        {
            var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);

            sut.OnUnhandledException(this, new UnhandledExceptionEventArgs(Thrown("kaboom"), isTerminating: true));
        }
        text = LogText();

        text.Should().Contain("[FTL]");
        text.Should().Contain("Unhandled exception on a thread; the application is terminating");
        text.Should().Contain("InvalidOperationException").And.Contain("kaboom");
        text.Should().Contain(" at ", "the stack trace is part of the entry");
    }

    [Fact]
    public void ANonFatalUnhandledException_IsAnError_NotACriticalOne()
    {
        string text;
        using (var provider = RealLogging())
        {
            var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);

            sut.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("survivable"), isTerminating: false));
        }
        text = LogText();

        text.Should().Contain("[ERR]").And.NotContain("terminating");
    }

    [Fact]
    public void AThrownObjectThatIsNotAnException_IsStillRecorded()
    {
        // .NET allows `throw` of non-Exception objects from other languages.
        string text;
        using (var provider = RealLogging())
        {
            var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);

            sut.OnUnhandledException(this, new UnhandledExceptionEventArgs("a plain string", isTerminating: true));
        }
        text = LogText();

        text.Should().Contain("a plain string");
    }

    [Fact]
    public void AnUnobservedTaskException_IsLogged_AndMarkedObserved()
    {
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("lost")));
        string text;
        using (var provider = RealLogging())
        {
            var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);

            sut.OnUnobservedTaskException(this, args);
        }
        text = LogText();

        text.Should().Contain("a task nobody awaited").And.Contain("lost");
        args.Observed.Should().BeTrue();
    }

    [Fact]
    public void AUiThreadException_IsLoggedAsCritical()
    {
        string text;
        using (var provider = RealLogging())
        {
            var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);

            sut.ReportUiThread(new NullReferenceException("in a click handler"));
        }
        text = LogText();

        text.Should().Contain("[FTL]").And.Contain("the UI thread").And.Contain("in a click handler");
    }

    [Fact]
    public void WithoutALogger_TheErrorGoesToTheFallbackFile()
    {
        // The case of a crash before the container exists, or after it has been disposed.
        var sut = new UnhandledExceptionLogging(() => null, _dir);

        sut.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("too early"), isTerminating: true));

        var text = File.ReadAllText(Path.Combine(_dir, "unhandled.log"));
        text.Should().Contain("[Critical]").And.Contain("too early").And.Contain("terminating");
    }

    [Fact]
    public void ALoggerThatItselfThrows_DoesNotHideTheOriginalError()
    {
        var sut = new UnhandledExceptionLogging(() => new ThrowingLogger(), _dir);

        var act = () => sut.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("the real problem"), isTerminating: true));

        act.Should().NotThrow();
        File.ReadAllText(Path.Combine(_dir, "unhandled.log")).Should().Contain("the real problem");
    }

    [Fact]
    public void WhenEvenTheFallbackCannotBeWritten_TheHandlerStillDoesNotThrow()
    {
        // A file where the folder should be: neither the logger nor the fallback can write.
        var blocker = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(blocker, "x");
        var sut = new UnhandledExceptionLogging(() => null, Path.Combine(blocker, "logs"));

        var act = () => sut.OnUnhandledException(this, new UnhandledExceptionEventArgs(new InvalidOperationException("x"), isTerminating: true));

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Install_ReallyHooksTheTaskScheduler_SoALostTaskErrorReachesTheLog()
    {
        // Proves the subscription itself, not just the method it points at. The runtime raises the
        // event from the finalizer thread when a faulted task is collected, hence the GC loop.
        string text = string.Empty;
        using var provider = RealLogging();
        var sut = new UnhandledExceptionLogging(() => provider.GetRequiredService<ILoggerFactory>().CreateLogger("Unhandled"), _dir);
        sut.Install();
        try
        {
            for (var attempt = 0; attempt < 40 && !text.Contains("wiring-check"); attempt++)
            {
                FaultAndForget("wiring-check");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(50, TestContext.Current.CancellationToken);
                text = LogText();
            }
        }
        finally
        {
            sut.Uninstall();
        }

        text.Should().Contain("a task nobody awaited").And.Contain("wiring-check");
    }

    // Kept out of line so the task is unreachable by the time the caller collects garbage.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultAndForget(string message)
    {
        var task = Task.Run(() => throw new InvalidOperationException(message));

        // Wait for it to fault without observing the exception: Wait(), Result and await all
        // observe it, which would make the task "handled" and the event would never fire.
        ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new IOException("disk full");
    }
}
