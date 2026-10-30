using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models.Entities;

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

    /// <summary>
    /// JSON-serialised <see cref="DiskConnection"/>: the structured parameters behind a network
    /// disk or an encrypted one. <see cref="FilePath"/> holds the URI derived from them, which is
    /// what QEMU is given; this keeps the pieces editable. Empty for a plain local file.
    /// </summary>
    public string ConnectionJson { get; set; } = string.Empty;
    public int Index { get; set; }

    public VirtualMachine? VirtualMachine { get; set; }
}
