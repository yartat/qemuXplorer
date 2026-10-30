using System.Diagnostics;
using FluentAssertions;
using QemuXplorer.Core.Services;
using QemuXplorer.Core.Tests.TestSupport;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class NetworkAndUsbArgumentTests
{
    private readonly QemuArgumentBuilder _sut = new();

    private static VirtualMachine Vm() => new()
    {
        Name = "t",
        Architecture = GuestArchitecture.X86_64,
        Cpu = new CpuConfig { Model = string.Empty },
        Memory = new MemoryConfig { SizeMB = 256 },
        Display = new DisplayConfig { Type = DisplayType.None }
    };

    private static QemuInstallation Qemu() => new() { BinaryDirectory = Path.GetTempPath() };

    private IReadOnlyList<string> Args(VirtualMachine vm) => _sut.Build(vm, Qemu()).Arguments;

    private IReadOnlyList<string> Net(NetworkAdapter nic)
    {
        var vm = Vm();
        vm.NetworkAdapters.Add(nic);
        return Args(vm);
    }

    private IReadOnlyList<string> Usb(params UsbDevice[] devices)
    {
        var vm = Vm();
        foreach (var d in devices) vm.UsbDevices.Add(d);
        return Args(vm);
    }

    private static string After(IReadOnlyList<string> args, string flag, int nth = 0)
    {
        var indexes = args.Select((a, i) => (a, i)).Where(x => x.a == flag).Select(x => x.i).ToList();
        return args[indexes[nth] + 1];
    }

    // ---- network: user ----

    [Fact]
    public void User_Default_IsJustTheBackendAndId() =>
        After(Net(new NetworkAdapter()), "-netdev").Should().Be("user,id=net0");

    [Fact]
    public void User_Options_AreOnlyWrittenWhenSet()
    {
        var netdev = After(Net(new NetworkAdapter
        {
            UserSubnet = "10.1.0.0/24", UserDhcpStart = "10.1.0.50", UserDns = "10.1.0.3", UserHostname = "guest", UserRestrict = true
        }), "-netdev");

        netdev.Should().Be("user,id=net0,net=10.1.0.0/24,dhcpstart=10.1.0.50,dns=10.1.0.3,hostname=guest,restrict=on");
    }

    [Fact]
    public void User_PortForwards_BecomeOneHostfwdEach()
    {
        var rules = PortForwardRules.ToJson([
            new PortForward("tcp", "127.0.0.1", 2222, "", 22),
            new PortForward("udp", "", 5353, "10.0.2.15", 53)
        ]);

        var netdev = After(Net(new NetworkAdapter { HostForwardingRules = rules }), "-netdev");

        netdev.Should().Be("user,id=net0,hostfwd=tcp:127.0.0.1:2222-:22,hostfwd=udp::5353-10.0.2.15:53");
    }

    [Fact]
    public void User_CommasInValues_AreDoubled() =>
        After(Net(new NetworkAdapter { UserHostname = "a,restrict=on" }), "-netdev").Should().Contain("hostname=a,,restrict=on");

    // ---- network: tap and bridge ----

    [Fact]
    public void Tap_WithoutScripts_RunsNone() =>
        After(Net(new NetworkAdapter { BackendType = NetworkBackendType.Tap, TapInterface = "tap0" }), "-netdev")
            .Should().Be("tap,id=net0,ifname=tap0,script=no,downscript=no");

    [Fact]
    public void Tap_WithScripts_PassesThem() =>
        After(Net(new NetworkAdapter
        {
            BackendType = NetworkBackendType.Tap, TapInterface = "tap0", TapScript = "/etc/up.sh", TapDownScript = "/etc/down.sh"
        }), "-netdev").Should().Be("tap,id=net0,ifname=tap0,script=/etc/up.sh,downscript=/etc/down.sh");

    [Fact]
    public void Tap_WithoutAName_LetsQemuChoose() =>
        After(Net(new NetworkAdapter { BackendType = NetworkBackendType.Tap }), "-netdev").Should().Be("tap,id=net0,script=no,downscript=no");

    [Fact]
    public void Bridge_NamesTheBridge_OrLeavesTheDefault()
    {
        After(Net(new NetworkAdapter { BackendType = NetworkBackendType.Bridge, BridgeInterface = "virbr0" }), "-netdev")
            .Should().Be("bridge,id=net0,br=virbr0");
        After(Net(new NetworkAdapter { BackendType = NetworkBackendType.Bridge }), "-netdev").Should().Be("bridge,id=net0");
    }

    // ---- network: socket ----

    [Theory]
    [InlineData(NetworkSocketMode.Listen, "socket,id=net0,listen=:1234")]
    [InlineData(NetworkSocketMode.Connect, "socket,id=net0,connect=:1234")]
    [InlineData(NetworkSocketMode.Multicast, "socket,id=net0,mcast=:1234")]
    public void Socket_Modes_UseTheirOwnKey(NetworkSocketMode mode, string expected) =>
        After(Net(new NetworkAdapter { BackendType = NetworkBackendType.Socket, SocketMode = mode, SocketPort = 1234 }), "-netdev")
            .Should().Be(expected);

    [Fact]
    public void Socket_Udp_NeedsALocalAddressToo() =>
        After(Net(new NetworkAdapter
        {
            BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Udp,
            SocketHost = "127.0.0.1", SocketPort = 1234, SocketLocalHost = "127.0.0.1", SocketLocalPort = 1235
        }), "-netdev").Should().Be("socket,id=net0,udp=127.0.0.1:1234,localaddr=127.0.0.1:1235");

    [Theory]
    [InlineData(NetworkModel.Vmxnet3, "vmxnet3,netdev=net0")]
    [InlineData(NetworkModel.Pcnet, "pcnet,netdev=net0")]
    public void NewModels_HaveTheirQemuNames(NetworkModel model, string expected) =>
        After(Net(new NetworkAdapter { Model = model }), "-device").Should().Be(expected);

    [Fact]
    public void EveryModel_HasACatalogEntryWithADescription()
    {
        NetworkModelCatalog.All.Select(m => m.Model).Should().BeEquivalentTo(Enum.GetValues<NetworkModel>());
        NetworkModelCatalog.All.Should().OnlyContain(m => m.Description.Length > 40 && m.Label.Length > 0);
        NetworkModelCatalog.All.Select(m => m.QemuName).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(NetworkModel.VirtIoNetPci, "virtio-net-pci")]
    [InlineData(NetworkModel.E1000, "e1000")]
    [InlineData(NetworkModel.E1000E, "e1000e")]
    [InlineData(NetworkModel.Rtl8139, "rtl8139")]
    [InlineData(NetworkModel.Ne2kPci, "ne2k_pci")]
    [InlineData(NetworkModel.Vmxnet3, "vmxnet3")]
    [InlineData(NetworkModel.Pcnet, "pcnet")]
    public void TheCommandLine_UsesTheCatalogsDeviceNames(NetworkModel model, string device) =>
        After(Net(new NetworkAdapter { Model = model }), "-device").Should().Be($"{device},netdev=net0");

    [Fact]
    public void ExistingModels_KeepTheirStoredIntegerValues()
    {
        // Stored as ints: appended members must not move the old ones.
        ((int)NetworkModel.VirtIoNetPci).Should().Be(0);
        ((int)NetworkModel.Ne2kPci).Should().Be(4);
        ((int)NetworkModel.Vmxnet3).Should().Be(5);
    }

    // ---- USB ----

    [Fact]
    public void Usb_ByVendorAndProduct_IsUnchanged() =>
        Usb(new UsbDevice { VendorId = 0x046D, ProductId = 0xC52B }).Should()
            .ContainInOrder("-usb", "-device", "usb-host,vendorid=0x046d,productid=0xc52b");

    [Fact]
    public void Usb_ByPort_UsesBusAndAddress() =>
        After(Usb(new UsbDevice { HostBus = 1, HostAddress = 7 }), "-device").Should().Be("usb-host,hostbus=1,hostaddr=7");

    [Fact]
    public void Usb_BootDevices_GetIncreasingBootIndexes()
    {
        var args = Usb(
            new UsbDevice { VendorId = 1, ProductId = 1, IsBootDevice = true },
            new UsbDevice { DeviceType = UsbDeviceType.Tablet, IsBootDevice = true },
            new UsbDevice { DeviceType = UsbDeviceType.Storage, ImagePath = "/i/s.img", IsBootDevice = true });

        args.Where(a => a.Contains("bootindex")).Should().Equal("usb-host,vendorid=0x0001,productid=0x0001,bootindex=1", "usb-storage,drive=usbdrive2,bootindex=2");
    }

    [Fact]
    public void Usb_Storage_DeclaresADriveThenTheDevice()
    {
        var args = Usb(new UsbDevice
        {
            DeviceType = UsbDeviceType.Storage, ImagePath = "/i/a,b.img", ImageFormat = DiskFormat.QCow2, IsReadOnly = true, IsRemovable = true
        });

        After(args, "-drive").Should().Be("if=none,id=usbdrive0,file=/i/a,,b.img,format=qcow2,readonly=on");
        After(args, "-device").Should().Be("usb-storage,drive=usbdrive0,removable=on");
        args.ToList().IndexOf("-drive").Should().BeLessThan(args.ToList().IndexOf("-device"));
    }

    [Theory]
    [InlineData(UsbDeviceType.Tablet, "usb-tablet")]
    [InlineData(UsbDeviceType.Mouse, "usb-mouse")]
    [InlineData(UsbDeviceType.Keyboard, "usb-kbd")]
    public void Usb_InputDevices_AreJustTheirName(UsbDeviceType type, string expected) =>
        After(Usb(new UsbDevice { DeviceType = type }), "-device").Should().Be(expected);

    [Fact]
    public void Usb_Audio_DeclaresItsOwnAudioBackend()
    {
        var args = Usb(new UsbDevice { DeviceType = UsbDeviceType.Audio, AudioDriver = "dsound" });

        After(args, "-audiodev").Should().Be("dsound,id=usbaudio0");
        After(args, "-device").Should().Be("usb-audio,audiodev=usbaudio0");
    }

    [Fact]
    public void Usb_AudioWithoutADriver_IsSilentRatherThanInvalid() =>
        After(Usb(new UsbDevice { DeviceType = UsbDeviceType.Audio }), "-audiodev").Should().Be("none,id=usbaudio0");

    [Theory]
    [InlineData(UsbSerialBackend.Null, "null,id=usbserial0")]
    [InlineData(UsbSerialBackend.File, "file,id=usbserial0,path=/tmp/out.log")]
    [InlineData(UsbSerialBackend.Tcp, "socket,id=usbserial0,host=127.0.0.1,port=4555,server=on,wait=off")]
    public void Usb_Serial_PicksItsChardev(UsbSerialBackend backend, string expected)
    {
        var args = Usb(new UsbDevice { DeviceType = UsbDeviceType.Serial, SerialBackend = backend, SerialTarget = "/tmp/out.log", SerialPort = 4555 });

        After(args, "-chardev").Should().Be(expected);
        After(args, "-device").Should().Be("usb-serial,chardev=usbserial0");
    }

    [Fact]
    public void Usb_SmartCard_IsAReaderWhoseCardArrivesOverTcp()
    {
        var args = Usb(new UsbDevice { DeviceType = UsbDeviceType.SmartCard, SerialPort = 2001 });

        After(args, "-chardev").Should().Be("socket,id=usbcard0,host=127.0.0.1,port=2001,server=on,wait=off");
        args.Should().ContainInOrder("-device", "usb-ccid", "-device", "ccid-card-passthru,chardev=usbcard0");
    }

    [Theory]
    [InlineData(UsbControllerModel.Xhci, "qemu-xhci")]
    [InlineData(UsbControllerModel.Ehci, "usb-ehci")]
    [InlineData(UsbControllerModel.Uhci, "piix3-usb-uhci")]
    public void Usb_AController_ReplacesTheDefaultOne(UsbControllerModel model, string expected)
    {
        var args = Usb(new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = model }, new UsbDevice { DeviceType = UsbDeviceType.Tablet });

        args.Should().NotContain("-usb");
        args.Where(a => a is "qemu-xhci" or "usb-ehci" or "piix3-usb-uhci" or "usb-tablet").Should().Equal(expected, "usb-tablet");
    }

    [Fact]
    public void EveryController_HasACatalogEntryWithADescription()
    {
        UsbControllerCatalog.All.Select(c => c.Model).Should().BeEquivalentTo(Enum.GetValues<UsbControllerModel>());
        UsbControllerCatalog.All.Should().OnlyContain(c => c.Description.Length > 40);
    }

    [Fact]
    public void EveryAudioDriverOffered_HasADescription_AndTheUsualOneComesFirst()
    {
        AudioDriverCatalog.ForThisSystem.Should().OnlyContain(d => d.Description.Length > 20 && !d.Description.StartsWith("A driver QEMU may offer"));
        AudioDriverCatalog.ForThisSystem[0].Name.Should().Be(OperatingSystem.IsWindows() ? "dsound" : OperatingSystem.IsMacOS() ? "coreaudio" : "pa");
        AudioDriverCatalog.ForThisSystem.Select(d => d.Name).Should().Contain("none");
    }

    [Fact]
    public void AnAudioDriverTheCatalogDoesNotKnow_StillGetsAnEntry() =>
        AudioDriverCatalog.Get("oss").Should().Match<AudioDriverInfo>(d => d.Name == "oss" && d.Description.Length > 0);

    [RealQemuSystemFact]
    public async Task Real_Qemu_HasEveryAudioDriverWeOfferOnThisSystem()
    {
        var psi = new ProcessStartInfo(RealQemuSystem.Path!, ["-audiodev", "help"])
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        var text = await proc.StandardOutput.ReadToEndAsync() + await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var available = text.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        AudioDriverCatalog.ForThisSystem.Select(d => d.Name).Should().OnlyContain(name => available.Contains(name));
    }

    [Fact]
    public void Usb_AnUnattachedEntry_IsLeftOut()
    {
        var args = Usb(new UsbDevice { DeviceType = UsbDeviceType.Tablet, IsPassthrough = false });

        args.Should().NotContain("usb-tablet");
    }

    [Fact]
    public void Usb_NoEntries_AddsNothing() => Args(Vm()).Should().NotContain("-usb");

    // ---- the real QEMU, where it is installed ----

    // QEMU checks every device and backend while it starts, so surviving a couple of seconds paused
    // (-S) means the whole command line was accepted; an invalid one exits at once with a message.
    private static async Task<string?> StartAndStopAsync(string binary, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(binary)
        {
            UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-S");
        psi.ArgumentList.Add("-nodefaults");

        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        var exited = await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(1.5)));
        if (proc.HasExited)
            return $"exit {proc.ExitCode}: {(await stderr).Trim()}";

        proc.Kill(entireProcessTree: true);
        return null;
    }

    public static IEnumerable<object[]> StartableDevices()
    {
        yield return [new NetworkAdapter { HostForwardingRules = PortForwardRules.ToJson([new PortForward("tcp", "127.0.0.1", 52222, "", 22)]), UserHostname = "g", UserRestrict = true }];
        foreach (var model in Enum.GetValues<NetworkModel>())
            yield return [new NetworkAdapter { Model = model }];
        yield return [new NetworkAdapter { BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Listen, SocketPort = 53341 }];
        // QEMU on Windows cannot bind a multicast group address ("can't bind ip=230.0.0.1 to socket").
        if (!OperatingSystem.IsWindows())
            yield return [new NetworkAdapter { BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Multicast, SocketHost = "230.0.0.1", SocketPort = 53342 }];
        yield return [new NetworkAdapter { BackendType = NetworkBackendType.Socket, SocketMode = NetworkSocketMode.Udp, SocketHost = "127.0.0.1", SocketPort = 53343, SocketLocalHost = "127.0.0.1", SocketLocalPort = 53344 }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Tablet }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Mouse }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Keyboard }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Audio, AudioDriver = "none" }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Serial, SerialBackend = UsbSerialBackend.Null }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Serial, SerialBackend = UsbSerialBackend.Tcp, SerialPort = 53345 }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.HostPassthrough, VendorId = 0x1234, ProductId = 0x5678 }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = UsbControllerModel.Xhci }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = UsbControllerModel.Ehci }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = UsbControllerModel.Uhci }];
        yield return [new UsbDevice { DeviceType = UsbDeviceType.SmartCard, SerialPort = 53346 }];
    }

    [RealQemuSystemTheory]
    [MemberData(nameof(StartableDevices))]
    public async Task Real_Qemu_AcceptsTheGeneratedCommandLine(object device)
    {
        var vm = Vm();
        switch (device)
        {
            case NetworkAdapter nic: vm.NetworkAdapters.Add(nic); break;
            case UsbDevice usb: vm.UsbDevices.Add(usb); break;
        }

        var failure = await StartAndStopAsync(RealQemuSystem.Path!, Args(vm));

        failure.Should().BeNull();
    }

    [RealQemuSystemFact]
    public async Task Real_Qemu_AcceptsAUsbStorageImage()
    {
        var image = Path.Combine(Path.GetTempPath(), "qx-usbstick-" + Guid.NewGuid().ToString("N") + ".img");
        File.WriteAllBytes(image, new byte[1024 * 1024]);
        try
        {
            var vm = Vm();
            vm.UsbDevices.Add(new UsbDevice { DeviceType = UsbDeviceType.Storage, ImagePath = image, ImageFormat = DiskFormat.Raw, IsRemovable = true, IsBootDevice = true });

            var failure = await StartAndStopAsync(RealQemuSystem.Path!, Args(vm));

            failure.Should().BeNull();
        }
        finally
        {
            File.Delete(image);
        }
    }
}
