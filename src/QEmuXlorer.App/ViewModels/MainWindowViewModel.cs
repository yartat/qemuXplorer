using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QEmuXlorer.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase _currentPage;

    private readonly DashboardViewModel _dashboard;
    private readonly VirtualMachinesViewModel _virtualMachines;
    private readonly QemuInstallationsViewModel _qemuInstallations;
    private readonly SettingsViewModel _settings;

    public MainWindowViewModel(
        DashboardViewModel dashboard,
        VirtualMachinesViewModel virtualMachines,
        QemuInstallationsViewModel qemuInstallations,
        SettingsViewModel settings)
    {
        _dashboard = dashboard;
        _virtualMachines = virtualMachines;
        _qemuInstallations = qemuInstallations;
        _settings = settings;
        _currentPage = dashboard;
    }

    [RelayCommand]
    private void NavigateToDashboard() => CurrentPage = _dashboard;

    [RelayCommand]
    private void NavigateToVirtualMachines() => CurrentPage = _virtualMachines;

    [RelayCommand]
    private void NavigateToQemuInstallations() => CurrentPage = _qemuInstallations;

    [RelayCommand]
    private void NavigateToSettings() => CurrentPage = _settings;
}
