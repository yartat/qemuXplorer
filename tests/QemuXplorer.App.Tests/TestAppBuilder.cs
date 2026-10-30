using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using QemuXplorer.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace QemuXplorer.App.Tests;

/// <summary>
/// Deliberately hosts a bare <see cref="Application"/> rather than QemuXplorer's own
/// <c>App</c>: that one builds the DI container and runs EF migrations against the real
/// user database at ~/.qemu_explorer on startup, which a test run must never touch.
/// Loading a view's XAML needs no application state beyond an Avalonia instance existing.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// The Fluent theme is loaded so controls get their templates: without it a ListBox renders no items,
/// and a test that clicks one has nothing to click. QemuXplorer's own theme is not needed for that.
/// </summary>
public sealed class HeadlessTestApp : Application
{
    public HeadlessTestApp() => Styles.Add(new FluentTheme());
}
