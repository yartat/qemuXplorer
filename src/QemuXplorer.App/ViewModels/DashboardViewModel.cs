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

    [ObservableProperty]
    private int _runningCount;

    [ObservableProperty]
    private int _definedCount;

    [ObservableProperty]
    private string _allocatedMemory = "0M";

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
        var all = await _vmService.GetAllAsync();
        var names = all.ToDictionary(v => v.Id, v => v.Name);
        var running = _vmService.GetRunningVms();

        RunningVms.Clear();
        foreach (var vm in running)
        {
            RunningVms.Add(new RunningVmRow(
                vm.VmId,
                names.TryGetValue(vm.VmId, out var name) ? name : "(deleted)",
                vm.ProcessId,
                vm.StartedAt,
                vm.IsMonitorConnected));
        }

        RunningCount = RunningVms.Count;
        DefinedCount = all.Count;

        // Only running machines actually hold memory.
        var runningIds = running.Select(r => r.VmId).ToHashSet();
        var totalMb = all.Where(v => runningIds.Contains(v.Id)).Sum(v => v.Memory.SizeMB);
        AllocatedMemory = FormatMemory(totalMb);

        StatusMessage = RunningVms.Count == 0
            ? "no machines running"
            : $"{RunningVms.Count} running";
    }

    private static string FormatMemory(long megabytes)
        => megabytes >= 1024
            ? $"{megabytes / 1024.0:0.#}G"
            : $"{megabytes}M";

    [RelayCommand]
    private async Task StopVmAsync(RunningVmRow? row)
    {
        if (row is null) return;

        await _vmService.StopAsync(row.VmId);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task KillVmAsync(RunningVmRow? row)
    {
        if (row is null) return;

        await _vmService.StopAsync(row.VmId, force: true);
        await RefreshAsync();
    }
}
