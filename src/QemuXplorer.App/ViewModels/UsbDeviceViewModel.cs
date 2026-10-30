using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.App.ViewModels;

public sealed record UsbTypeChoice(UsbDeviceType Type, string Title, string Description);

/// <summary>
/// The dialog behind both "Add device" and "Edit" on the USB tab. Adding walks three steps (type, settings,
/// review); editing opens on the settings of the entry's own type, which cannot change, so the form only
/// shows what that type has.
/// </summary>
public partial class UsbDeviceViewModel : ViewModelBase
{
    private const int LastStep = 2;

    private static readonly string[] DerivedProperties =
    [
        nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(CanGoBack), nameof(IsLastStep), nameof(ShowNext),
        nameof(IsHost), nameof(IsStorage), nameof(IsAudio), nameof(IsSerial), nameof(IsSmartCard), nameof(IsController),
        nameof(IsInput), nameof(HasNoSettings), nameof(CanBoot), nameof(SerialIsFile), nameof(SerialIsTcp), nameof(ByPort),
        nameof(ValidationMessage), nameof(HasValidationMessage), nameof(CanGoNext), nameof(Warning), nameof(HasWarning),
        nameof(CommandPreview), nameof(Summary), nameof(StepTitle), nameof(DialogTitle), nameof(TypeDescription),
        nameof(CanEditType), nameof(HostNote), nameof(HasHostNote), nameof(FormatIsReadOnly), nameof(CanEditReadOnly), nameof(ImageFormatDescription),
        nameof(SelectedAudioDriver), nameof(AudioDriverDescription), nameof(SelectedController), nameof(ControllerDescription)
    ];

    public static IReadOnlyList<UsbTypeChoice> Types { get; } =
    [
        new(UsbDeviceType.HostPassthrough, "Device from this computer",
            "Hand a USB device that is plugged in here to the guest (passthrough). The guest gets exclusive use of it."),
        new(UsbDeviceType.Storage, "USB stick from an image file",
            "Present a disk image as a USB mass-storage device."),
        new(UsbDeviceType.Tablet, "Tablet (absolute pointer)",
            "Keeps the guest's pointer exactly under yours. Needed for a usable mouse over VNC and SPICE."),
        new(UsbDeviceType.Mouse, "Mouse", "A plain relative USB mouse."),
        new(UsbDeviceType.Keyboard, "Keyboard", "A USB keyboard."),
        new(UsbDeviceType.Audio, "Audio device", "A USB sound card, played through an audio driver on this computer."),
        new(UsbDeviceType.Serial, "Serial port", "A USB serial adapter whose data goes to a file or a TCP port."),
        new(UsbDeviceType.SmartCard, "Smart card reader", "A CCID reader that gets its card from another program over TCP."),
        new(UsbDeviceType.Controller, "USB controller",
            "Choose the machine's USB host controller (USB 3, 2 or 1.1) instead of the default one.")
    ];

    public IReadOnlyList<UsbTypeChoice> TypeChoices => Types;
    public IReadOnlyList<UsbControllerInfo> ControllerChoices => UsbControllerCatalog.All;
    public UsbSerialBackend[] SerialBackends { get; } = Enum.GetValues<UsbSerialBackend>();
    public IReadOnlyList<DiskFormatInfo> ImageFormats { get; } = DiskFormatCatalog.All.Where(f => f.Format != DiskFormat.Luks).ToList();
    public ObservableCollection<HostUsbDevice> HostDevices { get; } = [];

    /// <summary>QEMU audio drivers worth offering on this computer, the usual one first. A stored driver the list lacks is added.</summary>
    public ObservableCollection<AudioDriverInfo> AudioDriverChoices { get; } = new(AudioDriverCatalog.ForThisSystem);

    public IReadOnlyList<string> AudioDrivers => AudioDriverChoices.Select(d => d.Name).ToList();

    private readonly UsbHostService _hosts;
    private UsbDevice _original = new();

    [ObservableProperty] private int _step;
    [ObservableProperty] private UsbTypeChoice _selectedType = Types[0];
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isAttached = true;
    [ObservableProperty] private bool _isBoot;

    // Host device
    [ObservableProperty] private HostUsbDevice? _selectedHostDevice;
    [ObservableProperty] private string _vendorIdText = string.Empty;
    [ObservableProperty] private string _productIdText = string.Empty;
    [ObservableProperty] private bool _useBusAndAddress;
    [ObservableProperty] private int _hostBus;
    [ObservableProperty] private int _hostAddress;
    [ObservableProperty] private string _hostListNote = string.Empty;
    [ObservableProperty] private bool _isLoadingHosts;

    // Storage
    [ObservableProperty] private string _imagePath = string.Empty;
    [ObservableProperty] private DiskFormatInfo? _imageFormat;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _isRemovable;

    // Audio
    [ObservableProperty] private string _audioDriver = string.Empty;

    // Serial and smart card
    [ObservableProperty] private UsbSerialBackend _serialBackend = UsbSerialBackend.Null;
    [ObservableProperty] private string _serialTarget = string.Empty;
    [ObservableProperty] private int _serialPort = 4555;

    // Controller
    [ObservableProperty] private UsbControllerModel _controller = UsbControllerModel.Xhci;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Set by the window: opens a file picker (save mode for a file to write).</summary>
    public Func<bool, string, Task<string?>>? FilePicker { get; set; }

    public bool IsEditing { get; private set; }
    public UsbDevice? Result { get; private set; }

    public event EventHandler? CloseRequested;

    public UsbDeviceViewModel(UsbHostService hosts)
    {
        _hosts = hosts;
        _audioDriver = AudioDriverChoices[0].Name;
        _imageFormat = ImageFormats.First(f => f.Format == DiskFormat.Raw);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || Array.IndexOf(DerivedProperties, e.PropertyName) >= 0)
                return;
            RaiseDerived();
        };
    }

    // ---- entry points ----

    public void InitializeForAdd()
    {
        IsEditing = false;
        _original = new UsbDevice();
        Step = 0;
        RaiseDerived();
    }

    public void InitializeForEdit(UsbDevice device)
    {
        IsEditing = true;
        _original = device;
        SelectedType = Types.First(t => t.Type == device.DeviceType);
        Description = device.Description;
        IsAttached = device.IsPassthrough;
        IsBoot = device.IsBootDevice;

        VendorIdText = device.VendorId == 0 ? string.Empty : device.VendorId.ToString("x4", CultureInfo.InvariantCulture);
        ProductIdText = device.ProductId == 0 ? string.Empty : device.ProductId.ToString("x4", CultureInfo.InvariantCulture);
        UseBusAndAddress = device.HostBus > 0;
        HostBus = device.HostBus;
        HostAddress = device.HostAddress;

        ImagePath = device.ImagePath;
        ImageFormat = ImageFormats.FirstOrDefault(f => f.Format == device.ImageFormat) ?? ImageFormat;
        IsReadOnly = device.IsReadOnly;
        IsRemovable = device.IsRemovable;

        var driver = device.AudioDriver.Length > 0 ? device.AudioDriver : AudioDriverChoices[0].Name;
        if (AudioDriverChoices.All(d => d.Name != driver))
            AudioDriverChoices.Add(AudioDriverCatalog.Get(driver));
        AudioDriver = driver;
        SerialBackend = device.SerialBackend;
        SerialTarget = device.SerialTarget;
        SerialPort = device.SerialPort > 0 ? device.SerialPort : SerialPort;
        Controller = device.Controller;

        Step = 1;
        RaiseDerived();
    }

    /// <summary>Fills the list of devices plugged into this computer. Safe to call again to refresh.</summary>
    public async Task LoadHostDevicesAsync()
    {
        IsLoadingHosts = true;
        try
        {
            var listing = await _hosts.ListAsync();
            HostDevices.Clear();
            foreach (var device in listing.Devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                HostDevices.Add(device);
            HostListNote = listing.Devices.Count == 0 && listing.Note.Length == 0
                ? "No USB devices were found. You can still type the ids."
                : listing.Note;
        }
        finally
        {
            IsLoadingHosts = false;
        }
    }

    partial void OnSelectedHostDeviceChanged(HostUsbDevice? value)
    {
        if (value is null) return;
        VendorIdText = value.VendorId.ToString("x4", CultureInfo.InvariantCulture);
        ProductIdText = value.ProductId.ToString("x4", CultureInfo.InvariantCulture);
        HostBus = value.Bus;
        HostAddress = value.Address;
        if (string.IsNullOrWhiteSpace(Description)) Description = value.Name;
    }

    partial void OnSelectedTypeChanged(UsbTypeChoice value)
    {
        if (!IsEditing && value.Type == UsbDeviceType.SmartCard && SerialPort == 4555)
            SerialPort = 2001;
    }

    partial void OnImageFormatChanged(DiskFormatInfo? value)
    {
        if (value?.IsReadOnly == true) IsReadOnly = true;
    }

    // ---- what the dialog shows ----

    public string DialogTitle => IsEditing ? $"Edit USB device - {SelectedType.Title}" : "Add USB device";

    public string StepTitle => IsEditing ? "Settings" : Step switch
    {
        0 => "Step 1 of 3 · Type",
        1 => "Step 2 of 3 · Settings",
        _ => "Step 3 of 3 · Review"
    };

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool CanEditType => !IsEditing;
    public bool CanGoBack => !IsEditing && Step > 0;
    public bool IsLastStep => IsEditing || Step == LastStep;
    public bool ShowNext => !IsLastStep;

    public bool IsHost => SelectedType.Type == UsbDeviceType.HostPassthrough;
    public bool IsStorage => SelectedType.Type == UsbDeviceType.Storage;
    public bool IsAudio => SelectedType.Type == UsbDeviceType.Audio;
    public bool IsSerial => SelectedType.Type == UsbDeviceType.Serial;
    public bool IsSmartCard => SelectedType.Type == UsbDeviceType.SmartCard;
    public bool IsController => SelectedType.Type == UsbDeviceType.Controller;
    public bool IsInput => SelectedType.Type is UsbDeviceType.Tablet or UsbDeviceType.Mouse or UsbDeviceType.Keyboard;
    public bool HasNoSettings => IsInput;
    public bool CanBoot => IsHost || IsStorage;
    public bool SerialIsFile => IsSerial && SerialBackend == UsbSerialBackend.File;
    public bool SerialIsTcp => IsSerial && SerialBackend == UsbSerialBackend.Tcp;
    public bool ByPort => IsHost && UseBusAndAddress;

    public string TypeDescription => SelectedType.Description;

    /// <summary>The audio driver as its catalog entry, for the list and its description.</summary>
    public AudioDriverInfo SelectedAudioDriver
    {
        get => AudioDriverChoices.FirstOrDefault(d => d.Name == AudioDriver) ?? AudioDriverCatalog.Get(AudioDriver);
        set { if (value is not null) AudioDriver = value.Name; }
    }

    public string AudioDriverDescription => SelectedAudioDriver.Description;

    /// <summary>The controller as its catalog entry, for the list and its description.</summary>
    public UsbControllerInfo SelectedController
    {
        get => UsbControllerCatalog.Get(Controller);
        set { if (value is not null) Controller = value.Model; }
    }

    public string ControllerDescription => SelectedController.Description;
    public bool FormatIsReadOnly => ImageFormat?.IsReadOnly ?? false;

    /// <summary>What the selected image format is, shown under the field.</summary>
    public string ImageFormatDescription => ImageFormat?.Description ?? string.Empty;
    public bool CanEditReadOnly => !FormatIsReadOnly;

    /// <summary>What passthrough needs on this operating system, since it is the type most likely to fail for outside reasons.</summary>
    public string HostNote => OperatingSystem.IsWindows()
        ? "On Windows, QEMU needs the device to use the WinUSB driver (for example installed with Zadig), and a QEMU built with libusb."
        : OperatingSystem.IsMacOS()
            ? "On macOS, QEMU usually needs to run with extra privileges to claim a device the system already uses."
            : "On Linux the user running QEMU needs write access to the device node (a udev rule), and the host must not be using the device.";

    public bool HasHostNote => IsHost;

    public string? ValidationMessage => Validate();
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool CanGoNext => Step != 1 || ValidationMessage is null;

    public string Warning
    {
        get
        {
            if (IsAudio && AudioDriver == "none")
                return "With the \"none\" driver the device exists in the guest but plays nowhere.";
            if (IsController)
                return "Controllers are PCI devices. Machines without PCI (such as the Arm and RISC-V virt boards) cannot use them.";
            if (IsSmartCard)
                return "The reader waits for a program such as vscclient to connect to the port and supply the card.";
            return string.Empty;
        }
    }

    public bool HasWarning => Warning.Length > 0;

    public string CommandPreview
    {
        get
        {
            if (Validate() is not null) return string.Empty;
            var args = QemuArgumentBuilder.UsbArguments([BuildDevice()]);
            return string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        }
    }

    public string Summary
    {
        get
        {
            var device = BuildDevice();
            var detail = device.Summary.Length > 0 ? $" · {device.Summary}" : string.Empty;
            return $"{device.DisplayName}{detail}" + (IsAttached ? string.Empty : "  (not attached)");
        }
    }

    // ---- validation ----

    private string? Validate()
    {
        switch (SelectedType.Type)
        {
            case UsbDeviceType.HostPassthrough:
                if (UseBusAndAddress)
                {
                    if (HostBus < 1) return "Enter the USB bus number.";
                    if (HostAddress < 1) return "Enter the device's address on that bus.";
                    return null;
                }
                if (!TryHex(VendorIdText, out _)) return "The vendor id must be 1 to 4 hexadecimal digits, like 046d.";
                if (!TryHex(ProductIdText, out _)) return "The product id must be 1 to 4 hexadecimal digits, like c52b.";
                return null;

            case UsbDeviceType.Storage:
                if (ImageFormat is null) return "Choose the image format.";
                if (string.IsNullOrWhiteSpace(ImagePath)) return "Choose the image file.";
                if (!File.Exists(ImagePath.Trim())) return "That image file does not exist. Create it first (for example in the Add disk wizard).";
                return null;

            case UsbDeviceType.Audio:
                return string.IsNullOrWhiteSpace(AudioDriver) ? "Choose an audio driver." : null;

            case UsbDeviceType.Serial:
                if (SerialBackend == UsbSerialBackend.File)
                    return string.IsNullOrWhiteSpace(SerialTarget) ? "Choose the file the port writes to." : null;
                if (SerialBackend == UsbSerialBackend.Tcp)
                    return SerialPort is < 1 or > 65535 ? "The port must be between 1 and 65535." : null;
                return null;

            case UsbDeviceType.SmartCard:
                return SerialPort is < 1 or > 65535 ? "The port must be between 1 and 65535." : null;
        }

        return null;
    }

    private static bool TryHex(string text, out ushort value)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[2..];
        return trimmed.Length is >= 1 and <= 4
            && ushort.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    // ---- building the result ----

    private UsbDevice BuildDevice()
    {
        var device = new UsbDevice
        {
            Id = _original.Id,
            VmId = _original.VmId,
            DeviceType = SelectedType.Type,
            IsPassthrough = IsAttached,
            Description = Description.Trim(),
            IsBootDevice = IsBoot && CanBoot
        };

        switch (SelectedType.Type)
        {
            case UsbDeviceType.HostPassthrough:
                if (UseBusAndAddress)
                {
                    device.HostBus = HostBus;
                    device.HostAddress = HostAddress;
                }
                else
                {
                    TryHex(VendorIdText, out var vendor);
                    TryHex(ProductIdText, out var product);
                    device.VendorId = vendor;
                    device.ProductId = product;
                }
                break;

            case UsbDeviceType.Storage:
                device.ImagePath = ImagePath.Trim();
                device.ImageFormat = ImageFormat?.Format ?? DiskFormat.Raw;
                device.IsReadOnly = IsReadOnly || FormatIsReadOnly;
                device.IsRemovable = IsRemovable;
                break;

            case UsbDeviceType.Audio:
                device.AudioDriver = AudioDriver.Trim();
                break;

            case UsbDeviceType.Serial:
                device.SerialBackend = SerialBackend;
                device.SerialTarget = SerialBackend == UsbSerialBackend.File ? SerialTarget.Trim() : string.Empty;
                device.SerialPort = SerialBackend == UsbSerialBackend.Tcp ? SerialPort : 0;
                break;

            case UsbDeviceType.SmartCard:
                device.SerialPort = SerialPort;
                break;

            case UsbDeviceType.Controller:
                device.Controller = Controller;
                break;
        }

        return device;
    }

    private void RaiseDerived()
    {
        foreach (var name in DerivedProperties)
            OnPropertyChanged(name);
    }

    // ---- commands ----

    [RelayCommand]
    private void Next()
    {
        if (Step == 1 && Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        StatusMessage = string.Empty;
        if (Step < LastStep) Step++;

        // The list of plugged-in devices is only read when someone gets to choose from it.
        if (Step == 1 && IsHost && HostDevices.Count == 0 && !IsLoadingHosts)
            _ = LoadHostDevicesAsync();
    }

    [RelayCommand]
    private void Back()
    {
        StatusMessage = string.Empty;
        if (Step > 0 && !IsEditing) Step--;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish()
    {
        if (Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        Result = BuildDevice();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private Task RefreshHostsAsync() => LoadHostDevicesAsync();

    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(false, "Choose the image file");
        if (!string.IsNullOrEmpty(picked)) ImagePath = picked;
    }

    [RelayCommand]
    private async Task BrowseSerialFileAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(true, "Write the serial output to");
        if (!string.IsNullOrEmpty(picked)) SerialTarget = picked;
    }
}
