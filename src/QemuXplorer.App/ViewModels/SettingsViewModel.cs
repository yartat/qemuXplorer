using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Data;
using QemuXplorer.Data.Repositories;

namespace QemuXplorer.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private const string ThemeKey = "Theme";
    private const string DefaultQemuPathKey = "DefaultQemuPath";
    private const string AutoStartMonitorKey = "AutoStartMonitor";

    private readonly IAppSettingRepository _settings;

    [ObservableProperty]
    private bool _isDarkTheme = true;

    [ObservableProperty]
    private string _defaultQemuPath = string.Empty;

    [ObservableProperty]
    private bool _autoStartMonitor;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string DatabasePath => DbPathHelper.GetDatabasePath();

    public SettingsViewModel(IAppSettingRepository settings)
    {
        _settings = settings;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsDarkTheme = !string.Equals(await _settings.GetValueAsync(ThemeKey), "Light", StringComparison.OrdinalIgnoreCase);
        DefaultQemuPath = await _settings.GetValueAsync(DefaultQemuPathKey) ?? string.Empty;
        AutoStartMonitor = string.Equals(await _settings.GetValueAsync(AutoStartMonitorKey), "true", StringComparison.OrdinalIgnoreCase);
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _settings.SetAsync(ThemeKey, IsDarkTheme ? "Dark" : "Light");
        await _settings.SetAsync(DefaultQemuPathKey, DefaultQemuPath);
        await _settings.SetAsync(AutoStartMonitorKey, AutoStartMonitor ? "true" : "false");
        StatusMessage = "settings saved";
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply at once so the checkbox is its own preview; Save persists it.
        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = value
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }
    }
}
