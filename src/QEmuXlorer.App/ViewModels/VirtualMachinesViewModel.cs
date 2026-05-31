using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.App.ViewModels;

public partial class VirtualMachinesViewModel : ViewModelBase
{
    private readonly VirtualMachineService _vmService;

    public ObservableCollection<VirtualMachine> VirtualMachines { get; } = [];

    [ObservableProperty]
    private VirtualMachine? _selectedVm;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public VirtualMachinesViewModel(VirtualMachineService vmService)
    {
        _vmService = vmService;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var vms = await _vmService.GetAllAsync();
        VirtualMachines.Clear();
        foreach (var vm in vms) VirtualMachines.Add(vm);
    }

    [RelayCommand]
    private async Task AddVmAsync()
    {
        var vm = new VirtualMachine { Name = "New VM" };
        await _vmService.AddAsync(vm);
        VirtualMachines.Add(vm);
        SelectedVm = vm;
    }

    [RelayCommand]
    private async Task DeleteVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        await _vmService.DeleteAsync(vm.Id);
        VirtualMachines.Remove(vm);
        if (SelectedVm == vm) SelectedVm = null;
    }

    [RelayCommand]
    private async Task CloneVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        var clone = await _vmService.CloneAsync(vm.Id, $"{vm.Name} (Clone)");
        VirtualMachines.Add(clone);
        SelectedVm = clone;
    }

    [RelayCommand]
    private async Task StartVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        try
        {
            await _vmService.StartAsync(vm.Id);
            StatusMessage = $"Started: {vm.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting VM: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        await _vmService.StopAsync(vm.Id);
        StatusMessage = $"Stopped: {vm.Name}";
    }
}
