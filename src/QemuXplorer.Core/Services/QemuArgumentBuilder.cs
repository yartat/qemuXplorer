using System.Text;
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
        args.Add("-cpu");
        args.Add(vm.Cpu.Model);
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
            var nic = vm.NetworkAdapters[i];
            var netId = $"net{i}";
            args.Add("-netdev");
            args.Add(BuildNetdev(nic, netId));

            var device = new StringBuilder(NetworkModelName(nic.Model));
            device.Append(",netdev=").Append(netId);
            if (!string.IsNullOrWhiteSpace(nic.MacAddress))
                device.Append(",mac=").Append(nic.MacAddress);
            args.Add("-device");
            args.Add(device.ToString());
        }

        // Display
        AddDisplay(args, vm.Display);

        // USB devices
        if (vm.UsbDevices.Count > 0)
        {
            args.Add("-usb");
            foreach (var usb in vm.UsbDevices.Where(u => u.IsPassthrough))
            {
                args.Add("-device");
                args.Add($"usb-host,vendorid=0x{usb.VendorId:x4},productid=0x{usb.ProductId:x4}");
            }
        }

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

        var drive = new StringBuilder($"file={disk.FilePath}");
        drive.Append(",format=").Append(FormatName(disk.Format));

        if (isNvme)
            drive.Append(",if=none,id=").Append(driveId);
        else
            drive.Append(",if=").Append(InterfaceName(disk.Interface)).Append(",index=").Append(disk.Index);

        drive.Append(",cache=").Append(CacheName(disk.CacheMode));
        if (disk.IsCdRom) drive.Append(",media=cdrom");
        if (disk.IsReadOnly) drive.Append(",readonly=on");
        if (disk.IsSnapshotMode) drive.Append(",snapshot=on");

        args.Add("-drive");
        args.Add(drive.ToString());

        if (isNvme)
        {
            args.Add("-device");
            args.Add($"nvme,drive={driveId},serial={driveId}");
        }
    }

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

    private static string GetBinaryName(GuestArchitecture arch)
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

    private static string FormatName(DiskFormat fmt) => fmt switch
    {
        DiskFormat.QCow2 => "qcow2",
        DiskFormat.Raw   => "raw",
        DiskFormat.Vmdk  => "vmdk",
        DiskFormat.Vhd   => "vpc",
        DiskFormat.Vhdx  => "vhdx",
        DiskFormat.Vdi   => "vdi",
        _                => "qcow2"
    };

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

    private static string NetworkModelName(NetworkModel model) => model switch
    {
        NetworkModel.VirtIoNetPci => "virtio-net-pci",
        NetworkModel.E1000        => "e1000",
        NetworkModel.E1000E       => "e1000e",
        NetworkModel.Rtl8139      => "rtl8139",
        NetworkModel.Ne2kPci      => "ne2k_pci",
        _                         => "virtio-net-pci"
    };

    private static string BuildNetdev(NetworkAdapter nic, string id) => nic.BackendType switch
    {
        NetworkBackendType.User   => $"user,id={id}",
        NetworkBackendType.Tap    => $"tap,id={id},ifname={nic.TapInterface},script=no,downscript=no",
        NetworkBackendType.Bridge => $"bridge,id={id},br={nic.BridgeInterface}",
        NetworkBackendType.Socket => $"socket,id={id}",
        _                         => $"user,id={id}"
    };

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
