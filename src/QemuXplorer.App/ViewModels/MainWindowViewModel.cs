using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace QemuXplorer.App.ViewModels;

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

    // The rail highlights the active page from these; there is no selection state to keep
    // in sync because CurrentPage is the single source of truth.
    public bool IsDashboard => CurrentPage == _dashboard;
    public bool IsVirtualMachines => CurrentPage == _virtualMachines;
    public bool IsQemuInstallations => CurrentPage == _qemuInstallations;
    public bool IsSettings => CurrentPage == _settings;

    partial void OnCurrentPageChanged(ViewModelBase value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsVirtualMachines));
        OnPropertyChanged(nameof(IsQemuInstallations));
        OnPropertyChanged(nameof(IsSettings));
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
