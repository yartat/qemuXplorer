using QEmuXlorer.Models.Dtos;
using QEmuXlorer.Models.Entities;
using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Core.Services;

public sealed class QemuArgumentBuilder : IQemuArgumentBuilder
{
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
        {
            var driveArg = $"file={disk.FilePath},format={FormatName(disk.Format)},if={InterfaceName(disk.Interface)},cache={CacheName(disk.CacheMode)},index={disk.Index}";
            if (disk.IsCdRom) driveArg += ",media=cdrom";
            if (disk.IsReadOnly) driveArg += ",readonly=on";
            if (disk.IsSnapshotMode) driveArg += ",snapshot=on";
            args.Add("-drive");
            args.Add(driveArg);
        }

        // Network adapters
        for (int i = 0; i < vm.NetworkAdapters.Count; i++)
        {
            var nic = vm.NetworkAdapters[i];
            var netId = $"net{i}";
            var netdevArg = BuildNetdev(nic, netId);
            args.Add("-netdev");
            args.Add(netdevArg);
            args.Add("-device");
            args.Add($"{NetworkModelName(nic.Model)},netdev={netId},mac={nic.MacAddress}");
        }

        // Display
        var disp = vm.Display;
        if (disp.Type != DisplayType.Default)
        {
            args.Add("-display");
            args.Add(DisplayTypeName(disp.Type));
        }
        if (disp.VgaModel != VgaType.None)
        {
            args.Add("-vga");
            args.Add(VgaTypeName(disp.VgaModel));
        }
        if (disp.Type == DisplayType.Vnc)
        {
            var addr = string.IsNullOrEmpty(disp.VncAddress) ? string.Empty : disp.VncAddress;
            args.Add("-vnc");
            args.Add($"{addr}:{disp.VncPort - 5900}");
        }
        if (disp.Type == DisplayType.Spice)
        {
            args.Add("-spice");
            args.Add($"port={disp.SpicePort},disable-ticketing=on");
        }
        if (disp.IsGLEnabled)
        {
            args.Add("-display");
            args.Add($"{DisplayTypeName(disp.Type)},gl=on");
        }
        if (disp.IsFullscreen)
        {
            args.Add("-full-screen");
        }

        // USB devices
        if (vm.UsbDevices.Count > 0)
        {
            args.Add("-usb");
            foreach (var usb in vm.UsbDevices)
            {
                args.Add("-device");
                args.Add($"usb-host,vendorid=0x{usb.VendorId:x4},productid=0x{usb.ProductId:x4}");
            }
        }

        // Extra arguments
        if (!string.IsNullOrWhiteSpace(vm.ExtraArguments))
        {
            args.AddRange(vm.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        var binary = Path.Combine(qemu.BinaryDirectory, GetBinaryName(vm.Architecture));
        return new QemuLaunchArgs(binary, args.AsReadOnly());
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
        DiskFormat.Vhd   => "vhd",
        DiskFormat.Vhdx  => "vhdx",
        DiskFormat.Vdi   => "vdi",
        _                => "qcow2"
    };

    private static string InterfaceName(DiskInterface iface) => iface switch
    {
        DiskInterface.VirtIo => "virtio",
        DiskInterface.Ide    => "ide",
        DiskInterface.Scsi   => "scsi",
        DiskInterface.Nvme   => "none",  // NVMe uses -device nvme separately; "none" for drive
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
        _              => "std"
    };
}
