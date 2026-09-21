using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QemuXplorer.App.ViewModels;
using QemuXplorer.App.Views;
using QemuXplorer.Core.Services;
using QemuXplorer.Data;
using QemuXplorer.Data.Repositories;

namespace QemuXplorer.App;

public class App : Application
{
    private ServiceProvider? _services;

    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();
        Services = _services;

        // Must complete before the first window binds to anything database-backed.
        DatabaseInitializer
            .InitializeAsync(_services.GetRequiredService<IDbContextFactory<QemuXplorerDbContext>>())
            .GetAwaiter()
            .GetResult();

        ApplyPersistedTheme().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = _services.GetRequiredService<MainWindowViewModel>()
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _services?.Dispose();
                _services = null;
                Services = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Without this the saved theme only took effect once the user opened the Settings page.
    /// </summary>
    private async Task ApplyPersistedTheme()
    {
        using var scope = _services!.CreateScope();
        var scopedServices = scope.ServiceProvider;
        var settings = scopedServices.GetRequiredService<IAppSettingRepository>();
        var theme = await settings.GetValueAsync("Theme");

        // Dark unless explicitly set to Light: the Control Room design is dark-first.
        RequestedThemeVariant = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // A pooled-style factory rather than a scoped DbContext: this app resolves everything
        // from the root provider, so a scoped context would live for the whole process and be
        // shared across threads. Each repository call gets its own short-lived context instead.
        services.AddDbContextFactory<QemuXplorerDbContext>(options =>
            options.UseSqlite($"Data Source={DbPathHelper.GetDatabasePath()}"));

        // Repositories and services are stateless over the factory, so they are safe as singletons.
        services.AddScoped<IVirtualMachineRepository, VirtualMachineRepository>();
        services.AddSingleton<IQemuInstallationRepository, QemuInstallationRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();

        services.AddSingleton<IQemuProcessManager, QemuProcessManager>();
        services.AddSingleton<IQemuArgumentBuilder, QemuArgumentBuilder>();
        services.AddScoped<VirtualMachineService>();
        services.AddSingleton<QemuDiscoveryService>();
        services.AddSingleton<DiskImageService>();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Page view models are singletons because MainWindowViewModel holds them for the
        // lifetime of the shell; resolving them transiently would discard page state on
        // every navigation.
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<VirtualMachinesViewModel>();
        services.AddSingleton<QemuInstallationsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        // The VM editor is a dialog: one instance per invocation.
        services.AddTransient<VmEditViewModel>();
    }
}
