namespace QEmuXlorer.Models.Dtos;

public record QemuLaunchArgs(string Binary, IReadOnlyList<string> Arguments);

public record RunningVm(Guid VmId, int ProcessId, DateTime StartedAt, bool IsMonitorConnected);

public record DiskImageInfo(string Path, Enums.DiskFormat Format, long SizeBytes, long ActualBytes);
