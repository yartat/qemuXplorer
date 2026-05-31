using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Models.Dtos;

namespace QEmuXlorer.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly VirtualMachineService _vmService;

    public ObservableCollection<RunningVm> RunningVms { get; } = [];

    [ObservableProperty]
    private RunningVm? _selectedRunningVm;

    public DashboardViewModel(VirtualMachineService vmService)
    {
        _vmService = vmService;
        if (_vmService.GetRunningVms() is { Count: > 0 } running)
            foreach (var vm in running) RunningVms.Add(vm);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        RunningVms.Clear();
        foreach (var vm in _vmService.GetRunningVms())
            RunningVms.Add(vm);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task StopVmAsync(RunningVm? vm)
    {
        if (vm is null) return;
        await _vmService.StopAsync(vm.VmId);
        RunningVms.Remove(vm);
    }
}
