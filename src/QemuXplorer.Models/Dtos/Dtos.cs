namespace QemuXplorer.Models.Dtos;

public record QemuLaunchArgs(string Binary, IReadOnlyList<string> Arguments);

public record RunningVm(Guid VmId, int ProcessId, DateTime StartedAt, bool IsMonitorConnected);

public record DiskImageInfo(string Path, Enums.DiskFormat Format, long SizeBytes, long ActualBytes, string? BackingFile = null);

/// <summary>A USB device plugged into this computer. Bus and address are 0 where the platform does not report them.</summary>
public record HostUsbDevice(ushort VendorId, ushort ProductId, string Name, string Manufacturer, int Bus, int Address)
{
    public string Ids => $"{VendorId:x4}:{ProductId:x4}";
    public string Label => $"{Name}  [{Ids}]";
}
