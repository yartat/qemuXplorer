using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QemuXplorer.App;

// Installed before anything else so that even a failure while the app is starting is recorded.
// The logger is looked up per report because the container does not exist yet at this point.
var unhandled = new UnhandledExceptionLogging(
    () => App.Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled"),
    LoggingOptions.Default.Directory);
unhandled.Install();

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .WithInterFont()
    .LogToTrace()
    .AfterSetup(_ => unhandled.InstallUiThreadHook())
    .StartWithClassicDesktopLifetime(args);
