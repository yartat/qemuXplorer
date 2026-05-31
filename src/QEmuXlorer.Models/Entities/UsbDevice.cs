namespace QEmuXlorer.Models.Entities;

public class UsbDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VmId { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsPassthrough { get; set; } = true;

    public VirtualMachine? VirtualMachine { get; set; }
}
