using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QemuXplorer.App;
using Xunit;

namespace QemuXplorer.App.Tests;

public class AppLoggingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-log-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] vars)
    {
        var map = vars.ToDictionary(v => v.Name, v => v.Value);
        return name => map.GetValueOrDefault(name);
    }

    /// <summary>Logs through a real provider built from the options, then flushes by disposing it.</summary>
    private string LogAndRead(LoggingOptions options, Action<ILoggerFactory> write)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => AppLogging.Configure(b, options));
        using (var provider = services.BuildServiceProvider())
            write(provider.GetRequiredService<ILoggerFactory>());

        return string.Concat(Directory.GetFiles(options.Directory, "qemuxplorer-*.log").Select(File.ReadAllText));
    }

    [Fact]
    public void ByDefault_LogsToAFile_NotTheConsole()
    {
        var options = LoggingOptions.FromEnvironment(Env());

        options.Console.Should().BeFalse();
        options.Level.Should().Be(LogLevel.Information);
        options.Directory.Should().EndWith("logs").And.Contain(".qemu_explorer");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData("banana", false)]
    public void TheConsole_IsOptIn(string value, bool expected)
    {
        LoggingOptions.FromEnvironment(Env((LoggingOptions.ConsoleVariable, value))).Console.Should().Be(expected);
    }

    [Theory]
    [InlineData("Debug", LogLevel.Debug)]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("Warning", LogLevel.Warning)]
    [InlineData("Error", LogLevel.Error)]
    [InlineData("nonsense", LogLevel.Information)]
    [InlineData("", LogLevel.Information)]
    [InlineData("None", LogLevel.Information)]
    [InlineData("99", LogLevel.Information)]
    public void TheLevel_FallsBackToInformationForAnythingUnusable(string value, LogLevel expected)
    {
        LoggingOptions.FromEnvironment(Env((LoggingOptions.LevelVariable, value))).Level.Should().Be(expected);
    }

    [Fact]
    public void Messages_AreWrittenToADatedFileWithLevelAndCategory()
    {
        var options = new LoggingOptions(_dir, LogLevel.Information, Console: false);

        var text = LogAndRead(options, f => f.CreateLogger("Some.Category").LogInformation("hello {Thing}", "world"));

        text.Should().Contain("[INF]").And.Contain("Some.Category").And.Contain("hello world");
        Directory.GetFiles(_dir).Single().Should().MatchRegex(@"qemuxplorer-\d{8}\.log$");
    }

    [Fact]
    public void Exceptions_AreWrittenWithTheirStackTrace()
    {
        var options = new LoggingOptions(_dir, LogLevel.Information, Console: false);

        var text = LogAndRead(options, f => f.CreateLogger("X").LogError(new InvalidOperationException("boom"), "it failed"));

        text.Should().Contain("it failed").And.Contain("InvalidOperationException").And.Contain("boom");
    }

    [Fact]
    public void BelowTheConfiguredLevel_NothingIsWritten()
    {
        var options = new LoggingOptions(_dir, LogLevel.Warning, Console: false);

        var text = LogAndRead(options, f =>
        {
            var log = f.CreateLogger("X");
            log.LogInformation("quiet info");
            log.LogWarning("loud warning");
        });

        text.Should().Contain("loud warning").And.NotContain("quiet info");
    }

    [Fact]
    public void EfCoreChatter_IsCappedAtWarning_AtTheDefaultLevel()
    {
        var options = new LoggingOptions(_dir, LogLevel.Information, Console: false);

        var text = LogAndRead(options, f =>
        {
            var ef = f.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
            ef.LogInformation("SELECT secret FROM everything");
            ef.LogWarning("a real EF warning");
        });

        text.Should().Contain("a real EF warning").And.NotContain("SELECT secret FROM everything");
    }

    [Fact]
    public void AtDebug_EfCoreIsShown_BecauseTheUserAskedToLook()
    {
        var options = new LoggingOptions(_dir, LogLevel.Debug, Console: false);

        var text = LogAndRead(options, f => f.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogInformation("SELECT 1"));

        text.Should().Contain("SELECT 1");
    }

    [Fact]
    public void TheConsoleProvider_IsRegisteredOnlyWhenAskedFor()
    {
        static bool HasConsole(LoggingOptions options)
        {
            var services = new ServiceCollection();
            services.AddLogging(b => AppLogging.Configure(b, options));
            using var provider = services.BuildServiceProvider();
            return provider.GetServices<ILoggerProvider>().Any(p => p.GetType().Name.Contains("Console"));
        }

        HasConsole(new LoggingOptions(_dir, LogLevel.Information, Console: false)).Should().BeFalse();
        HasConsole(new LoggingOptions(_dir, LogLevel.Information, Console: true)).Should().BeTrue();
    }

    [Fact]
    public void AnUnwritableFolder_DoesNotStopTheAppFromLogging()
    {
        // A file where the folder should be: creating or writing there is impossible.
        var blocker = Path.Combine(_dir, "not-a-folder");
        File.WriteAllText(blocker, "x");
        var options = new LoggingOptions(Path.Combine(blocker, "logs"), LogLevel.Information, Console: false);

        var services = new ServiceCollection();
        services.AddLogging(b => AppLogging.Configure(b, options));
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ILoggerFactory>().CreateLogger("X").LogError("nowhere to go");

        act.Should().NotThrow();
    }
}
