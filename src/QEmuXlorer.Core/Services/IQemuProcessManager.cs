using QEmuXlorer.Models.Dtos;

namespace QEmuXlorer.Core.Services;

public interface IQemuProcessManager
{
    event EventHandler<Guid>? VmExited;

    Task StartAsync(QemuLaunchArgs args, Guid vmId, CancellationToken ct = default);
    Task StopAsync(Guid vmId, bool force = false, CancellationToken ct = default);
    bool IsRunning(Guid vmId);
    IReadOnlyList<RunningVm> GetRunningVms();
}
