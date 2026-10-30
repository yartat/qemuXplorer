using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models.Entities;

public class UsbDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VmId { get; set; }

    /// <summary>What this entry is. Rows from before the type existed read as <see cref="UsbDeviceType.HostPassthrough"/>.</summary>
    public UsbDeviceType DeviceType { get; set; } = UsbDeviceType.HostPassthrough;

    /// <summary>False leaves the entry out of the command line without deleting it.</summary>
    public bool IsPassthrough { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    /// <summary>Boot from this device (<c>bootindex</c>). Passthrough and storage only.</summary>
    public bool IsBootDevice { get; set; }

    // Host passthrough: by vendor and product, or, when HostBus is set, by the physical port.
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public int HostBus { get; set; }
    public int HostAddress { get; set; }

    // Storage
    public string ImagePath { get; set; } = string.Empty;
    public DiskFormat ImageFormat { get; set; } = DiskFormat.Raw;
    public bool IsReadOnly { get; set; }
    public bool IsRemovable { get; set; }

    // Audio: a QEMU audio driver such as dsound, coreaudio, pa or none. Blank means none.
    public string AudioDriver { get; set; } = string.Empty;

    // Serial port: the TCP port to listen on. A smart card reader uses it as the port a card is passed in on.
    public UsbSerialBackend SerialBackend { get; set; } = UsbSerialBackend.Null;
    public string SerialTarget { get; set; } = string.Empty;
    public int SerialPort { get; set; }

    // Controller
    public UsbControllerModel Controller { get; set; } = UsbControllerModel.Xhci;

    public VirtualMachine? VirtualMachine { get; set; }

    /// <summary>One line for lists. Computed, so it is not stored.</summary>
    public string Summary => DeviceType switch
    {
        UsbDeviceType.HostPassthrough => HostBus > 0
            ? $"bus {HostBus}, address {HostAddress}"
            : $"{VendorId:x4}:{ProductId:x4}",
        UsbDeviceType.Storage => ImagePath.Length > 0 ? Path.GetFileName(ImagePath) : "(no image)",
        UsbDeviceType.Audio => AudioDriver.Length > 0 ? AudioDriver : "no audio output",
        UsbDeviceType.Serial => SerialBackend switch
        {
            UsbSerialBackend.File => $"file {SerialTarget}",
            UsbSerialBackend.Tcp => $"tcp port {SerialPort}",
            _ => "discarded"
        },
        UsbDeviceType.SmartCard => $"card on tcp port {SerialPort}",
        UsbDeviceType.Controller => Controller switch
        {
            UsbControllerModel.Xhci => "USB 3 (xHCI)",
            UsbControllerModel.Ehci => "USB 2 (EHCI)",
            _ => "USB 1.1 (UHCI)"
        },
        _ => string.Empty
    };

    /// <summary>The name shown in lists: the description if there is one, else the type.</summary>
    public string DisplayName => Description.Length > 0 ? Description : DeviceType switch
    {
        UsbDeviceType.HostPassthrough => "Host device",
        UsbDeviceType.Storage => "USB storage",
        UsbDeviceType.Tablet => "USB tablet",
        UsbDeviceType.Mouse => "USB mouse",
        UsbDeviceType.Keyboard => "USB keyboard",
        UsbDeviceType.Audio => "USB audio",
        UsbDeviceType.Serial => "USB serial port",
        UsbDeviceType.SmartCard => "Smart card reader",
        _ => "USB controller"
    };
}
