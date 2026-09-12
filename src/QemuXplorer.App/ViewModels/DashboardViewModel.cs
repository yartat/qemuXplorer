using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;

namespace QemuXplorer.App.ViewModels;

/// <summary>A running VM joined with its configured name for display.</summary>
public sealed record RunningVmRow(
    Guid VmId,
    string Name,
    int ProcessId,
    DateTime StartedAt,
    bool IsMonitorConnected);

public partial class DashboardViewModel : ViewModelBase
{
    private readonly VirtualMachineService _vmService;

    public ObservableCollection<RunningVmRow> RunningVms { get; } = [];

    [ObservableProperty]
    private RunningVmRow? _selectedRunningVm;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public DashboardViewModel(VirtualMachineService vmService)
    {
        _vmService = vmService;
        _vmService.VmExited += OnVmExited;
    }

    // Process.Exited is raised on a thread-pool thread; the collection is bound to the UI.
    private void OnVmExited(object? sender, Guid vmId)
        => Dispatcher.UIThread.Post(() => RefreshCommand.Execute(null));

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var names = (await _vmService.GetAllAsync()).ToDictionary(v => v.Id, v => v.Name);

        RunningVms.Clear();
        foreach (var vm in _vmService.GetRunningVms())
        {
            RunningVms.Add(new RunningVmRow(
                vm.VmId,
                names.TryGetValue(vm.VmId, out var name) ? name : "(deleted)",
                vm.ProcessId,
                vm.StartedAt,
                vm.IsMonitorConnected));
        }

        StatusMessage = RunningVms.Count == 0
            ? "No virtual machines are running."
            : $"{RunningVms.Count} virtual machine(s) running.";
    }

    [RelayCommand]
    private async Task StopVmAsync(RunningVmRow? row)
    {
        if (row is null) return;

        await _vmService.StopAsync(row.VmId);
        await RefreshAsync();
    }
}
