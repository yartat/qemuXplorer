using FluentAssertions;
using Xunit;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Models.Entities;
using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Core.Tests.Services;

public class QemuArgumentBuilderTests
{
    private static VirtualMachine DefaultVm(Action<VirtualMachine>? configure = null)
    {
        var vm = new VirtualMachine
        {
            Name = "TestVM",
            MachineType = "q35",
            Architecture = GuestArchitecture.X86_64,
        };
        vm.Cpu.Model = "host";
        vm.Cpu.Count = 2;
        vm.Cpu.Sockets = 1;
        vm.Cpu.Cores = 2;
        vm.Cpu.Threads = 1;
        vm.Memory.SizeMB = 2048;
        vm.Display.Type = DisplayType.Default;
        vm.Display.VgaModel = VgaType.Std;
        configure?.Invoke(vm);
        return vm;
    }

    private static QemuInstallation DefaultQemu() => new()
    {
        BinaryDirectory = "/usr/bin",
        DetectedVersion = "9.0.0"
    };

    private readonly QemuArgumentBuilder _sut = new();

    [Fact]
    public void Build_BasicVm_ContainsMachineFlag()
    {
        var args = _sut.Build(DefaultVm(), DefaultQemu());
        args.Arguments.Should().Contain(a => a.StartsWith("-machine"));
    }

    [Fact]
    public void Build_BasicVm_ContainsCpuFlag()
    {
        var args = _sut.Build(DefaultVm(), DefaultQemu());
        args.Arguments.Should().Contain("-cpu").And.Contain("host");
    }

    [Fact]
    public void Build_BasicVm_ContainsMemoryFlag()
    {
        var args = _sut.Build(DefaultVm(), DefaultQemu());
        args.Arguments.Should().Contain("-m").And.Contain("2048M");
    }

    [Fact]
    public void Build_BasicVm_ContainsSmpFlag()
    {
        var args = _sut.Build(DefaultVm(), DefaultQemu());
        args.Arguments.Should().Contain("-smp");
    }

    [Fact]
    public void Build_WithKvm_AccelContainsKvm()
    {
        var vm = DefaultVm(v => v.EnableKvm = true);
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain(a => a.Contains("kvm"));
    }

    [Fact]
    public void Build_WithDisk_ContainsDriveFlag()
    {
        var vm = DefaultVm();
        vm.Disks.Add(new DiskDevice
        {
            FilePath = "/images/disk.qcow2",
            Format = DiskFormat.QCow2,
            Interface = DiskInterface.VirtIo,
            Index = 0
        });
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain("-drive");
        args.Arguments.Should().Contain(a => a.Contains("/images/disk.qcow2"));
    }

    [Fact]
    public void Build_WithNetwork_ContainsNetdevFlag()
    {
        var vm = DefaultVm();
        vm.NetworkAdapters.Add(new NetworkAdapter
        {
            BackendType = NetworkBackendType.User,
            Model = NetworkModel.VirtIoNetPci,
            MacAddress = "52:54:00:12:34:56"
        });
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain("-netdev");
        args.Arguments.Should().Contain("-device");
    }

    [Fact]
    public void Build_WithUsbDevice_ContainsUsbHostDevice()
    {
        var vm = DefaultVm();
        vm.UsbDevices.Add(new UsbDevice
        {
            VendorId = 0x046D,
            ProductId = 0xC52B,
            IsPassthrough = true
        });
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain("-usb");
    }

    [Fact]
    public void Build_X86_64_CorrectBinaryName()
    {
        var args = _sut.Build(DefaultVm(), DefaultQemu());
        args.Binary.Should().Contain("qemu-system-x86_64");
    }

    [Fact]
    public void Build_Aarch64Architecture_CorrectBinaryName()
    {
        var vm = DefaultVm(v => v.Architecture = GuestArchitecture.Aarch64);
        var args = _sut.Build(vm, DefaultQemu());
        args.Binary.Should().Contain("qemu-system-aarch64");
    }

    [Fact]
    public void Build_WithVnc_ContainsVncFlag()
    {
        var vm = DefaultVm(v =>
        {
            v.Display.Type = DisplayType.Vnc;
            v.Display.VncPort = 5901;
        });
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain("-vnc");
    }

    [Fact]
    public void Build_ExtraArguments_AppendedToList()
    {
        var vm = DefaultVm(v => v.ExtraArguments = "-monitor stdio -no-reboot");
        var args = _sut.Build(vm, DefaultQemu());
        args.Arguments.Should().Contain("-monitor").And.Contain("stdio");
        args.Arguments.Should().Contain("-no-reboot");
    }
}
