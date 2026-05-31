using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QEmuXlorer.App.ViewModels;
using QEmuXlorer.App.Views;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Data;
using QEmuXlorer.Data.Repositories;

namespace QEmuXlorer.App;

public class App : Application
{
    private ServiceProvider? _services;

    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();
        Services = _services;

        // Initialize database
        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QEmuXplorerDbContext>();
            await DatabaseInitializer.InitializeAsync(db);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = mainVm };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database
        services.AddDbContext<QEmuXplorerDbContext>(options =>
            options.UseSqlite($"Data Source={DbPathHelper.GetDatabasePath()}"),
            ServiceLifetime.Scoped);

        // Repositories
        services.AddScoped<IVirtualMachineRepository, VirtualMachineRepository>();
        services.AddScoped<IQemuInstallationRepository, QemuInstallationRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();

        // Core services
        services.AddSingleton<IQemuProcessManager, QemuProcessManager>();
        services.AddScoped<IQemuArgumentBuilder, QemuArgumentBuilder>();
        services.AddScoped<VirtualMachineService>();
        services.AddScoped<QemuDiscoveryService>();
        services.AddScoped<DiskImageService>();

        // Logging
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // ViewModels
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<VirtualMachinesViewModel>();
        services.AddTransient<QemuInstallationsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<VmEditViewModel>();
        services.AddTransient<MainWindowViewModel>();
    }
}
