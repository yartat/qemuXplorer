using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Models.Entities;

public class DiskDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VmId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DiskFormat Format { get; set; } = DiskFormat.QCow2;
    public DiskInterface Interface { get; set; } = DiskInterface.VirtIo;
    public DiskCacheMode CacheMode { get; set; } = DiskCacheMode.WriteBack;
    public bool IsBootDevice { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsCdRom { get; set; }
    public bool IsSnapshotMode { get; set; }
    public int Index { get; set; }

    public VirtualMachine? VirtualMachine { get; set; }
}
