namespace QemuXplorer.Models.Enums;

/// <summary>What a USB entry of a machine is. Only ever append members: the value is stored as an integer.</summary>
public enum UsbDeviceType
{
    /// <summary>A real device on this computer, handed to the guest (<c>usb-host</c>).</summary>
    HostPassthrough,
    /// <summary>A disk image presented as a USB stick (<c>usb-storage</c>).</summary>
    Storage,
    /// <summary>Absolute pointer; keeps the mouse in step over VNC and SPICE (<c>usb-tablet</c>).</summary>
    Tablet,
    Mouse,
    Keyboard,
    Audio,
    Serial,
    SmartCard,
    /// <summary>The machine's USB host controller itself.</summary>
    Controller
}

public enum UsbControllerModel
{
    /// <summary>USB 3, the right choice for nearly everything (<c>qemu-xhci</c>).</summary>
    Xhci,
    /// <summary>USB 2 (<c>usb-ehci</c>).</summary>
    Ehci,
    /// <summary>USB 1.1 (<c>piix3-usb-uhci</c>).</summary>
    Uhci
}

/// <summary>Where a USB serial port's data goes.</summary>
public enum UsbSerialBackend
{
    /// <summary>Discard output, produce no input.</summary>
    Null,
    /// <summary>Write to a file.</summary>
    File,
    /// <summary>Accept one TCP connection on a port.</summary>
    Tcp
}
