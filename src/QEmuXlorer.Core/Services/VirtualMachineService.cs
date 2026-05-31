using QEmuXlorer.Data.Repositories;
using QEmuXlorer.Models.Dtos;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Core.Services;

public sealed class VirtualMachineService
{
    private readonly IVirtualMachineRepository _vmRepo;
    private readonly IQemuInstallationRepository _qemuRepo;
    private readonly IQemuArgumentBuilder _argBuilder;
    private readonly IQemuProcessManager _processManager;

    public VirtualMachineService(
        IVirtualMachineRepository vmRepo,
        IQemuInstallationRepository qemuRepo,
        IQemuArgumentBuilder argBuilder,
        IQemuProcessManager processManager)
    {
        _vmRepo = vmRepo;
        _qemuRepo = qemuRepo;
        _argBuilder = argBuilder;
        _processManager = processManager;
    }

    public Task<IReadOnlyList<VirtualMachine>> GetAllAsync(CancellationToken ct = default)
        => _vmRepo.GetAllAsync(ct);

    public Task<VirtualMachine?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _vmRepo.GetByIdAsync(id, ct);

    public Task AddAsync(VirtualMachine vm, CancellationToken ct = default)
        => _vmRepo.AddAsync(vm, ct);

    public Task UpdateAsync(VirtualMachine vm, CancellationToken ct = default)
        => _vmRepo.UpdateAsync(vm, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _vmRepo.DeleteAsync(id, ct);

    public async Task<VirtualMachine> CloneAsync(Guid sourceId, string newName, CancellationToken ct = default)
    {
        var source = await _vmRepo.GetByIdAsync(sourceId, ct)
            ?? throw new InvalidOperationException($"VM {sourceId} not found.");

        var clone = new VirtualMachine
        {
            Name = newName,
            Description = source.Description,
            Architecture = source.Architecture,
            MachineType = source.MachineType,
            EnableKvm = source.EnableKvm,
            EnableHvf = source.EnableHvf,
            EnableWhpx = source.EnableWhpx,
            FallbackToTcg = source.FallbackToTcg,
            ExtraArguments = source.ExtraArguments,
            IsTemplate = false,
            Cpu = new() { Model = source.Cpu.Model, Count = source.Cpu.Count, Sockets = source.Cpu.Sockets, Cores = source.Cpu.Cores, Threads = source.Cpu.Threads },
            Memory = new() { SizeMB = source.Memory.SizeMB, HotplugSlots = source.Memory.HotplugSlots },
            Display = new() { Type = source.Display.Type, VgaModel = source.Display.VgaModel, VncPort = source.Display.VncPort, SpicePort = source.Display.SpicePort, IsFullscreen = source.Display.IsFullscreen, IsGLEnabled = source.Display.IsGLEnabled }
        };

        await _vmRepo.AddAsync(clone, ct);
        return clone;
    }

    public async Task StartAsync(Guid vmId, CancellationToken ct = default)
    {
        var vm = await _vmRepo.GetByIdAsync(vmId, ct)
            ?? throw new InvalidOperationException($"VM {vmId} not found.");
        var qemu = await _qemuRepo.GetDefaultAsync(ct)
            ?? throw new InvalidOperationException("No default QEMU installation configured.");

        var launchArgs = _argBuilder.Build(vm, qemu);
        await _processManager.StartAsync(launchArgs, vmId, ct);
    }

    public Task StopAsync(Guid vmId, bool force = false, CancellationToken ct = default)
        => _processManager.StopAsync(vmId, force, ct);

    public bool IsRunning(Guid vmId) => _processManager.IsRunning(vmId);

    public IReadOnlyList<RunningVm> GetRunningVms() => _processManager.GetRunningVms();

    public async Task<QemuLaunchArgs?> PreviewCommandAsync(Guid vmId, CancellationToken ct = default)
    {
        var vm = await _vmRepo.GetByIdAsync(vmId, ct);
        var qemu = await _qemuRepo.GetDefaultAsync(ct);
        if (vm is null || qemu is null) return null;
        return _argBuilder.Build(vm, qemu);
    }
}
