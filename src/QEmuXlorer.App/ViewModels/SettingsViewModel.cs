using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QEmuXlorer.Data.Repositories;

namespace QEmuXlorer.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingRepository _settings;

    [ObservableProperty]
    private bool _isDarkTheme;

    public SettingsViewModel(IAppSettingRepository settings)
    {
        _settings = settings;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var theme = await _settings.GetValueAsync("Theme");
        IsDarkTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task SaveThemeAsync()
    {
        await _settings.SetAsync("Theme", IsDarkTheme ? "Dark" : "Light");
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply theme variant at runtime
        if (Avalonia.Application.Current is not null)
        {
            Avalonia.Application.Current.RequestedThemeVariant = value
                ? Avalonia.Styling.ThemeVariant.Dark
                : Avalonia.Styling.ThemeVariant.Light;
        }
    }
}
