using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Models.Entities;

public class VirtualMachine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GuestArchitecture Architecture { get; set; } = GuestArchitecture.X86_64;
    public string MachineType { get; set; } = "q35";
    public bool EnableKvm { get; set; }
    public bool EnableHvf { get; set; }
    public bool EnableWhpx { get; set; }
    public bool FallbackToTcg { get; set; } = true;
    public string ExtraArguments { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public bool IsTemplate { get; set; }

    // Owned types (flattened into same table)
    public CpuConfig Cpu { get; set; } = new();
    public MemoryConfig Memory { get; set; } = new();
    public DisplayConfig Display { get; set; } = new();

    // Navigation collections
    public List<DiskDevice> Disks { get; set; } = [];
    public List<NetworkAdapter> NetworkAdapters { get; set; } = [];
    public List<UsbDevice> UsbDevices { get; set; } = [];
}
