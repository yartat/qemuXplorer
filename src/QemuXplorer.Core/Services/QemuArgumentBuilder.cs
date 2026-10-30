using System.Text;
using QemuXplorer.Models;
using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

public sealed class QemuArgumentBuilder : IQemuArgumentBuilder
{
    private const int VncBasePort = 5900;

    public QemuLaunchArgs Build(VirtualMachine vm, QemuInstallation qemu)
    {
        var args = new List<string>();

        // Machine type + accelerators
        var accels = new List<string>();
        if (vm.EnableKvm) accels.Add("kvm");
        if (vm.EnableHvf) accels.Add("hvf");
        if (vm.EnableWhpx) accels.Add("whpx");
        if (vm.FallbackToTcg) accels.Add("tcg");

        var accelStr = accels.Count > 0 ? $",accel={string.Join(":", accels)}" : string.Empty;
        args.Add("-machine");
        args.Add($"type={vm.MachineType}{accelStr}");

        // CPU
        // A blank model is "the machine's own default", which QEMU picks when -cpu is absent.
        if (!string.IsNullOrWhiteSpace(vm.Cpu.Model))
        {
            args.Add("-cpu");
            args.Add(vm.Cpu.Model);
        }
        args.Add("-smp");
        args.Add($"{vm.Cpu.Count},sockets={vm.Cpu.Sockets},cores={vm.Cpu.Cores},threads={vm.Cpu.Threads}");

        // Memory
        args.Add("-m");
        args.Add($"{vm.Memory.SizeMB}M");

        // Disks
        foreach (var disk in vm.Disks.OrderBy(d => d.Index))
            AddDisk(args, disk);

        // Boot order
        var bootDisk = vm.Disks.OrderBy(d => d.Index).FirstOrDefault(d => d.IsBootDevice);
        if (bootDisk is not null)
        {
            args.Add("-boot");
            args.Add($"order={(bootDisk.IsCdRom ? "d" : "c")}");
        }

        // Network adapters
        for (int i = 0; i < vm.NetworkAdapters.Count; i++)
        {
            args.AddRange(NetworkArguments(vm.NetworkAdapters[i], i));
        }

        // Display
        AddDisplay(args, vm.Display);

        AddUsb(args, vm.UsbDevices);

        // Extra arguments
        if (!string.IsNullOrWhiteSpace(vm.ExtraArguments))
            args.AddRange(SplitArguments(vm.ExtraArguments));

        var binary = Path.Combine(qemu.BinaryDirectory, GetBinaryName(vm.Architecture));
        return new QemuLaunchArgs(binary, args.AsReadOnly());
    }

    private static void AddDisk(List<string> args, DiskDevice disk)
    {
        // NVMe is not an "if=" target: the drive is declared anonymously and bound to an
        // explicit -device nvme, otherwise the drive is attached to nothing.
        var isNvme = disk.Interface == DiskInterface.Nvme;
        var driveId = $"drive{disk.Index}";

        // A LUKS passphrase never goes on the command line: it is read from a file through a
        // secret object that the drive then references by id.
        var keyFile = DiskConnection.FromJson(disk.ConnectionJson)?.KeySecretFile;
        var secretId = $"sec{disk.Index}";
        var hasSecret = disk.Format == DiskFormat.Luks && !string.IsNullOrWhiteSpace(keyFile);
        if (hasSecret)
        {
            args.Add("-object");
            args.Add($"secret,id={secretId},file={EscapeOption(keyFile!)}");
        }

        var drive = new StringBuilder($"file={EscapeOption(disk.FilePath)}");
        drive.Append(",format=").Append(DiskFormatCatalog.QemuName(disk.Format));
        if (hasSecret) drive.Append(",key-secret=").Append(secretId);

        if (isNvme)
            drive.Append(",if=none,id=").Append(driveId);
        else
            drive.Append(",if=").Append(InterfaceName(disk.Interface)).Append(",index=").Append(disk.Index);

        drive.Append(",cache=").Append(CacheName(disk.CacheMode));
        if (disk.IsCdRom) drive.Append(",media=cdrom");

        // bochs, cloop, dmg and parallels cannot be written by QEMU, so asking for them
        // read-write would only fail at start-up.
        if (disk.IsReadOnly || DiskFormatCatalog.IsReadOnly(disk.Format)) drive.Append(",readonly=on");
        if (disk.IsSnapshotMode) drive.Append(",snapshot=on");

        args.Add("-drive");
        args.Add(drive.ToString());

        if (isNvme)
        {
            args.Add("-device");
            args.Add($"nvme,drive={driveId},serial={driveId}");
        }
    }

    /// <summary>The <c>-netdev</c> and <c>-device</c> pair for one adapter. Public so the editor can preview it.</summary>
    public static IReadOnlyList<string> NetworkArguments(NetworkAdapter nic, int index)
    {
        var netId = $"net{index}";
        var device = new StringBuilder(NetworkModelName(nic.Model));
        device.Append(",netdev=").Append(netId);
        if (!string.IsNullOrWhiteSpace(nic.MacAddress))
            device.Append(",mac=").Append(nic.MacAddress);

        return ["-netdev", BuildNetdev(nic, netId), "-device", device.ToString()];
    }

    /// <summary>Everything the USB entries add to the command line. Public so the editor can preview one.</summary>
    public static IReadOnlyList<string> UsbArguments(IReadOnlyCollection<UsbDevice> devices)
    {
        var args = new List<string>();
        AddUsb(args, devices);
        return args;
    }

    private static void AddUsb(List<string> args, IReadOnlyCollection<UsbDevice> devices)
    {
        if (devices.Count == 0)
            return;

        // "Attached" is the old IsPassthrough flag, now meaning "include this entry" for every type.
        var attached = devices.Where(d => d.IsPassthrough).ToList();

        // An explicit controller replaces the machine's default one (-usb); two would only compete.
        var controllers = attached.Where(d => d.DeviceType == UsbDeviceType.Controller).ToList();
        if (controllers.Count == 0)
            args.Add("-usb");
        foreach (var controller in controllers)
        {
            args.Add("-device");
            args.Add(UsbControllerName(controller.Controller));
        }

        var bootIndex = 0;
        for (var i = 0; i < attached.Count; i++)
        {
            var usb = attached[i];
            var canBoot = usb.DeviceType is UsbDeviceType.HostPassthrough or UsbDeviceType.Storage;
            var boot = usb.IsBootDevice && canBoot ? $",bootindex={++bootIndex}" : string.Empty;

            switch (usb.DeviceType)
            {
                case UsbDeviceType.HostPassthrough:
                    args.Add("-device");
                    args.Add(usb.HostBus > 0
                        ? $"usb-host,hostbus={usb.HostBus},hostaddr={usb.HostAddress}{boot}"
                        : $"usb-host,vendorid=0x{usb.VendorId:x4},productid=0x{usb.ProductId:x4}{boot}");
                    break;

                case UsbDeviceType.Storage:
                    var driveId = $"usbdrive{i}";
                    var drive = new StringBuilder($"if=none,id={driveId},file={EscapeOption(usb.ImagePath)}");
                    drive.Append(",format=").Append(DiskFormatCatalog.QemuName(usb.ImageFormat));
                    if (usb.IsReadOnly || DiskFormatCatalog.IsReadOnly(usb.ImageFormat)) drive.Append(",readonly=on");
                    args.Add("-drive");
                    args.Add(drive.ToString());
                    args.Add("-device");
                    args.Add($"usb-storage,drive={driveId}{(usb.IsRemovable ? ",removable=on" : string.Empty)}{boot}");
                    break;

                case UsbDeviceType.Tablet:
                    args.Add("-device");
                    args.Add("usb-tablet");
                    break;

                case UsbDeviceType.Mouse:
                    args.Add("-device");
                    args.Add("usb-mouse");
                    break;

                case UsbDeviceType.Keyboard:
                    args.Add("-device");
                    args.Add("usb-kbd");
                    break;

                case UsbDeviceType.Audio:
                    var audioId = $"usbaudio{i}";
                    args.Add("-audiodev");
                    args.Add($"{(string.IsNullOrWhiteSpace(usb.AudioDriver) ? "none" : usb.AudioDriver.Trim())},id={audioId}");
                    args.Add("-device");
                    args.Add($"usb-audio,audiodev={audioId}");
                    break;

                case UsbDeviceType.Serial:
                    var charId = $"usbserial{i}";
                    args.Add("-chardev");
                    args.Add(usb.SerialBackend switch
                    {
                        UsbSerialBackend.File => $"file,id={charId},path={EscapeOption(usb.SerialTarget)}",
                        UsbSerialBackend.Tcp => $"socket,id={charId},host=127.0.0.1,port={usb.SerialPort},server=on,wait=off",
                        _ => $"null,id={charId}"
                    });
                    args.Add("-device");
                    args.Add($"usb-serial,chardev={charId}");
                    break;

                case UsbDeviceType.SmartCard:
                    // The reader waits on a TCP port for a card from outside (vscclient). The emulated card
                    // (backend=nss-emulated) was tried first and QEMU refused it: it needs an NSS database.
                    var cardId = $"usbcard{i}";
                    args.Add("-chardev");
                    args.Add($"socket,id={cardId},host=127.0.0.1,port={usb.SerialPort},server=on,wait=off");
                    args.Add("-device");
                    args.Add("usb-ccid");
                    args.Add("-device");
                    args.Add($"ccid-card-passthru,chardev={cardId}");
                    break;
            }
        }
    }

    private static string UsbControllerName(UsbControllerModel model) => UsbControllerCatalog.QemuName(model);

    // In QEMU option strings a literal comma is written as two.
    private static string EscapeOption(string value) => value.Replace(",", ",,");

    private static void AddDisplay(List<string> args, DisplayConfig disp)
    {
        switch (disp.Type)
        {
            case DisplayType.Vnc:
                // -vnc takes a display number, not a TCP port.
                args.Add("-vnc");
                args.Add($"{disp.VncAddress}:{Math.Max(0, disp.VncPort - VncBasePort)}");
                break;

            case DisplayType.Spice:
                args.Add("-spice");
                args.Add($"port={disp.SpicePort},disable-ticketing=on");
                break;

            case DisplayType.Default:
                // Let QEMU pick the platform default.
                break;

            default:
                args.Add("-display");
                args.Add(disp.IsGLEnabled
                    ? $"{DisplayTypeName(disp.Type)},gl=on"
                    : DisplayTypeName(disp.Type));
                break;
        }

        args.Add("-vga");
        args.Add(VgaTypeName(disp.VgaModel));

        if (disp.IsFullscreen && disp.Type is not (DisplayType.None or DisplayType.Vnc or DisplayType.Spice))
            args.Add("-full-screen");
    }

    /// <summary>
    /// Splits a raw argument string on whitespace, honouring single and double quotes so that
    /// values such as -append "root=/dev/sda1 ro" survive as a single argument.
    /// </summary>
    private static IEnumerable<string> SplitArguments(string input)
    {
        var token = new StringBuilder();
        var quote = '\0';

        foreach (var ch in input)
        {
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else token.Append(ch);
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    token.Clear();
                }
            }
            else
            {
                token.Append(ch);
            }
        }

        if (token.Length > 0)
            yield return token.ToString();
    }

    /// <summary>The qemu-system-* file name for an architecture, with .exe on Windows.</summary>
    public static string GetBinaryName(GuestArchitecture arch)
    {
        var name = arch switch
        {
            GuestArchitecture.X86_64  => "qemu-system-x86_64",
            GuestArchitecture.I386    => "qemu-system-i386",
            GuestArchitecture.Aarch64 => "qemu-system-aarch64",
            GuestArchitecture.Arm     => "qemu-system-arm",
            GuestArchitecture.RiscV64 => "qemu-system-riscv64",
            GuestArchitecture.RiscV32 => "qemu-system-riscv32",
            GuestArchitecture.Ppc64   => "qemu-system-ppc64",
            GuestArchitecture.Ppc     => "qemu-system-ppc",
            GuestArchitecture.Mips    => "qemu-system-mips",
            GuestArchitecture.Mips64  => "qemu-system-mips64",
            GuestArchitecture.S390x   => "qemu-system-s390x",
            _                         => "qemu-system-x86_64"
        };
        return OperatingSystem.IsWindows() ? name + ".exe" : name;
    }

    private static string InterfaceName(DiskInterface iface) => iface switch
    {
        DiskInterface.VirtIo => "virtio",
        DiskInterface.Ide    => "ide",
        DiskInterface.Scsi   => "scsi",
        DiskInterface.Nvme   => "none",
        DiskInterface.Sd     => "sd",
        _                    => "virtio"
    };

    private static string CacheName(DiskCacheMode mode) => mode switch
    {
        DiskCacheMode.WriteBack    => "writeback",
        DiskCacheMode.WriteThrough => "writethrough",
        DiskCacheMode.None         => "none",
        DiskCacheMode.DirectSync   => "directsync",
        DiskCacheMode.Unsafe       => "unsafe",
        _                          => "writeback"
    };

    // The device names live in one place, NetworkModelCatalog, so the descriptions shown next to them cannot drift.
    private static string NetworkModelName(NetworkModel model) => NetworkModelCatalog.QemuName(model);

    private static string BuildNetdev(NetworkAdapter nic, string id) => nic.BackendType switch
    {
        NetworkBackendType.User   => BuildUserNetdev(nic, id),
        NetworkBackendType.Tap    => BuildTapNetdev(nic, id),
        NetworkBackendType.Bridge => string.IsNullOrWhiteSpace(nic.BridgeInterface)
            ? $"bridge,id={id}"
            : $"bridge,id={id},br={EscapeOption(nic.BridgeInterface.Trim())}",
        NetworkBackendType.Socket => BuildSocketNetdev(nic, id),
        _                         => $"user,id={id}"
    };

    private static string BuildUserNetdev(NetworkAdapter nic, string id)
    {
        var netdev = new StringBuilder($"user,id={id}");
        AppendIfSet(netdev, "net", nic.UserSubnet);
        AppendIfSet(netdev, "dhcpstart", nic.UserDhcpStart);
        AppendIfSet(netdev, "dns", nic.UserDns);
        AppendIfSet(netdev, "hostname", nic.UserHostname);
        if (nic.UserRestrict) netdev.Append(",restrict=on");
        foreach (var rule in PortForwardRules.Parse(nic.HostForwardingRules))
            netdev.Append(",hostfwd=").Append(EscapeOption(rule.ToHostFwd()));
        return netdev.ToString();
    }

    // script=no keeps QEMU from running a helper nobody asked for; a script the user chose is passed through.
    private static string BuildTapNetdev(NetworkAdapter nic, string id)
    {
        var netdev = new StringBuilder($"tap,id={id}");
        AppendIfSet(netdev, "ifname", nic.TapInterface);
        netdev.Append(",script=").Append(string.IsNullOrWhiteSpace(nic.TapScript) ? "no" : EscapeOption(nic.TapScript.Trim()));
        netdev.Append(",downscript=").Append(string.IsNullOrWhiteSpace(nic.TapDownScript) ? "no" : EscapeOption(nic.TapDownScript.Trim()));
        return netdev.ToString();
    }

    private static string BuildSocketNetdev(NetworkAdapter nic, string id)
    {
        var endpoint = $"{nic.SocketHost.Trim()}:{nic.SocketPort}";
        var netdev = new StringBuilder($"socket,id={id}");
        switch (nic.SocketMode)
        {
            case NetworkSocketMode.Connect:
                netdev.Append(",connect=").Append(EscapeOption(endpoint));
                break;
            case NetworkSocketMode.Multicast:
                netdev.Append(",mcast=").Append(EscapeOption(endpoint));
                break;
            case NetworkSocketMode.Udp:
                netdev.Append(",udp=").Append(EscapeOption(endpoint));
                netdev.Append(",localaddr=").Append(EscapeOption($"{nic.SocketLocalHost.Trim()}:{nic.SocketLocalPort}"));
                break;
            default:
                netdev.Append(",listen=").Append(EscapeOption(endpoint));
                break;
        }
        return netdev.ToString();
    }

    private static void AppendIfSet(StringBuilder builder, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            builder.Append(',').Append(key).Append('=').Append(EscapeOption(value.Trim()));
    }

    private static string DisplayTypeName(DisplayType type) => type switch
    {
        DisplayType.Gtk          => "gtk",
        DisplayType.Sdl          => "sdl",
        DisplayType.Vnc          => "vnc",
        DisplayType.Spice        => "spice-app",
        DisplayType.EglHeadless  => "egl-headless",
        DisplayType.None         => "none",
        _                        => "default"
    };

    private static string VgaTypeName(VgaType vga) => vga switch
    {
        VgaType.Std    => "std",
        VgaType.Vmware => "vmware",
        VgaType.Qxl    => "qxl",
        VgaType.Virtio => "virtio",
        VgaType.Cirrus => "cirrus",
        VgaType.None   => "none",
        _              => "std"
    };
}
