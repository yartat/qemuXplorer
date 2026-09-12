using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.App.ViewModels;

public partial class VirtualMachinesViewModel : ViewModelBase
{
    private readonly VirtualMachineService _vmService;

    public ObservableCollection<VirtualMachine> VirtualMachines { get; } = [];

    [ObservableProperty]
    private VirtualMachine? _selectedVm;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string CountSummary => VirtualMachines.Count == 1
        ? "1 defined"
        : $"{VirtualMachines.Count} defined";

    public VirtualMachinesViewModel(VirtualMachineService vmService)
    {
        _vmService = vmService;
        VirtualMachines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountSummary));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var selectedId = SelectedVm?.Id;

        var vms = await _vmService.GetAllAsync();
        VirtualMachines.Clear();
        foreach (var vm in vms) VirtualMachines.Add(vm);

        SelectedVm = selectedId is null
            ? null
            : VirtualMachines.FirstOrDefault(v => v.Id == selectedId);

        StatusMessage = $"{VirtualMachines.Count} machine(s) loaded";
    }

    [RelayCommand]
    private async Task AddVmAsync()
    {
        var vm = new VirtualMachine { Name = "New machine" };
        await _vmService.AddAsync(vm);
        VirtualMachines.Add(vm);
        SelectedVm = vm;
        StatusMessage = $"Created {vm.Name}";
    }

    [RelayCommand]
    private async Task DeleteVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        await _vmService.DeleteAsync(vm.Id);
        VirtualMachines.Remove(vm);
        if (SelectedVm == vm) SelectedVm = null;
        StatusMessage = $"Deleted {vm.Name}";
    }

    [RelayCommand]
    private async Task CloneVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        var clone = await _vmService.CloneAsync(vm.Id, $"{vm.Name} (Clone)");
        VirtualMachines.Add(clone);
        SelectedVm = clone;
        StatusMessage = $"Cloned to {clone.Name}";
    }

    [RelayCommand]
    private async Task StartVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        try
        {
            await _vmService.StartAsync(vm.Id);
            StatusMessage = $"Started {vm.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not start {vm.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        await _vmService.StopAsync(vm.Id);
        StatusMessage = $"Stopped {vm.Name}";
    }
}
