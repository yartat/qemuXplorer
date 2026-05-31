using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QEmuXlorer.Models.Dtos;

namespace QEmuXlorer.Core.Services;

public sealed class QemuProcessManager : IQemuProcessManager, IDisposable
{
    private readonly ILogger<QemuProcessManager> _logger;
    private readonly ConcurrentDictionary<Guid, (Process Process, RunningVm Info)> _running = new();

    public event EventHandler<Guid>? VmExited;

    public QemuProcessManager(ILogger<QemuProcessManager> logger) => _logger = logger;

    public async Task StartAsync(QemuLaunchArgs args, Guid vmId, CancellationToken ct = default)
    {
        if (_running.ContainsKey(vmId))
            throw new InvalidOperationException($"VM {vmId} is already running.");

        var psi = new ProcessStartInfo
        {
            FileName = args.Binary,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = false
        };
        foreach (var arg in args.Arguments)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) => OnProcessExited(vmId, process);

        process.Start();

        var info = new RunningVm(vmId, process.Id, DateTime.UtcNow, false);
        _running[vmId] = (process, info);
        _logger.LogInformation("Started VM {VmId} (PID {Pid})", vmId, process.Id);

        await Task.CompletedTask;
    }

    public async Task StopAsync(Guid vmId, bool force = false, CancellationToken ct = default)
    {
        if (!_running.TryGetValue(vmId, out var entry))
            return;

        if (force || entry.Process.HasExited)
        {
            if (!entry.Process.HasExited)
                entry.Process.Kill();
        }
        else
        {
            // Send SIGTERM / gentle close
            entry.Process.CloseMainWindow();
            await Task.Delay(3000, ct);
            if (!entry.Process.HasExited)
                entry.Process.Kill();
        }
    }

    public bool IsRunning(Guid vmId)
        => _running.TryGetValue(vmId, out var entry) && !entry.Process.HasExited;

    public IReadOnlyList<RunningVm> GetRunningVms()
        => _running.Values
            .Where(e => !e.Process.HasExited)
            .Select(e => e.Info)
            .ToList()
            .AsReadOnly();

    private void OnProcessExited(Guid vmId, Process process)
    {
        _running.TryRemove(vmId, out _);
        _logger.LogInformation("VM {VmId} exited with code {ExitCode}", vmId, process.ExitCode);
        VmExited?.Invoke(this, vmId);
    }

    public void Dispose()
    {
        foreach (var (process, _) in _running.Values)
        {
            try { process.Dispose(); } catch { /* best effort */ }
        }
        _running.Clear();
    }
}
