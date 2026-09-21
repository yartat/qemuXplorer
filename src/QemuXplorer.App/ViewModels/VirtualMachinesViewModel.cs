using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.App.ViewModels;

public partial class VirtualMachinesViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ObservableCollection<VirtualMachine> VirtualMachines { get; } = [];

    [ObservableProperty]
    private VirtualMachine? _selectedVm;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string CountSummary => VirtualMachines.Count == 1
        ? "1 defined"
        : $"{VirtualMachines.Count} defined";

    public VirtualMachinesViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        VirtualMachines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountSummary));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var selectedId = SelectedVm?.Id;
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        var vms = await vmService.GetAllAsync();
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
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        await vmService.AddAsync(vm);
        VirtualMachines.Add(vm);
        SelectedVm = vm;
        StatusMessage = $"Created {vm.Name}";
    }

    [RelayCommand]
    private async Task DeleteVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        await vmService .DeleteAsync(vm.Id);
        VirtualMachines.Remove(vm);
        if (SelectedVm == vm) SelectedVm = null;
        StatusMessage = $"Deleted {vm.Name}";
    }

    [RelayCommand]
    private async Task CloneVmAsync(VirtualMachine? vm)
    {
        if (vm is null) return;
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        var clone = await vmService.CloneAsync(vm.Id, $"{vm.Name} (Clone)");
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
            using var scope = _scopeFactory.CreateScope();
            var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
            await vmService.StartAsync(vm.Id);
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
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        await vmService.StopAsync(vm.Id);
        StatusMessage = $"Stopped {vm.Name}";
    }
}
