using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.Core.Services;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class UsbHostServiceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("qx-sysfs-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void SysfsDevice(string name, string? vendor, string? product, string? manufacturer = null, string? productName = null, string? bus = null, string? dev = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        void Write(string file, string? text) { if (text is not null) File.WriteAllText(Path.Combine(directory, file), text + "\n"); }
        Write("idVendor", vendor);
        Write("idProduct", product);
        Write("manufacturer", manufacturer);
        Write("product", productName);
        Write("busnum", bus);
        Write("devnum", dev);
    }

    // ---- Linux ----

    [Fact]
    public void Linux_ReadsDevicesAndSkipsRootHubsAndInterfaces()
    {
        SysfsDevice("usb1", "1d6b", "0002", "Linux Foundation", "xHCI Host Controller", "1", "1");
        SysfsDevice("1-2", "046d", "c52b", "Logitech", "USB Receiver", "1", "7");
        // A ':' is not allowed in a Windows folder name, where this test also runs.
        if (!OperatingSystem.IsWindows())
            SysfsDevice("1-2:1.0", "046d", "c52b");
        SysfsDevice("1-4", "8087", "0033", null, null, "1", "9");

        var devices = UsbHostService.ParseLinux(_root);

        devices.Select(d => (d.VendorId, d.ProductId, d.Bus, d.Address)).Should().Equal(
            ((ushort)0x046d, (ushort)0xc52b, 1, 7),
            ((ushort)0x8087, (ushort)0x0033, 1, 9));
        devices[0].Name.Should().Be("Logitech USB Receiver");
        devices[1].Name.Should().Be("USB device");
        devices[0].Ids.Should().Be("046d:c52b");
    }

    [Theory]
    [InlineData("1-2:1.0", false)]
    [InlineData("usb1", false)]
    [InlineData("1-2", true)]
    [InlineData("3-1.4.2", true)]
    public void Linux_OnlyDevicesAreListed_NotInterfacesOrRootHubs(string entry, bool listed) =>
        UsbHostService.IsLinuxDeviceEntry(entry).Should().Be(listed);

    [Fact]
    public void Linux_SkipsAnEntryWithoutIds_AndAMissingFolder()
    {
        SysfsDevice("1-3", null, null, "Nobody", "Half written");

        UsbHostService.ParseLinux(_root).Should().BeEmpty();
        UsbHostService.ParseLinux(Path.Combine(_root, "missing")).Should().BeEmpty();
    }

    // ---- Windows: output captured from a real machine ----

    private const string WindowsList =
        """[{"Name":"USB Input Device","PNPDeviceID":"USB\\VID_046D&PID_C077\\5&21DC7979&0&9","Manufacturer":"(Standard system devices)"},{"Name":"USB Composite Device","PNPDeviceID":"USB\\VID_0408&PID_546D\\0001","Manufacturer":"(Standard USB Host Controller)"},{"Name":"Intel(R) Wireless Bluetooth(R)","PNPDeviceID":"USB\\VID_8087&PID_0033\\5&21DC7979&0&10","Manufacturer":"Intel Corporation"}]""";

    [Fact]
    public void Windows_ReadsVendorAndProductFromThePnpId()
    {
        var devices = UsbHostService.ParseWindows(WindowsList);

        devices.Select(d => d.Ids).Should().Equal("046d:c077", "0408:546d", "8087:0033");
        devices[2].Name.Should().Be("Intel(R) Wireless Bluetooth(R)");
        devices[2].Manufacturer.Should().Be("Intel Corporation");
        devices.Should().OnlyContain(d => d.Bus == 0 && d.Address == 0, "Windows does not report them");
    }

    [Fact]
    public void Windows_ASingleDeviceArrivesAsAnObjectNotAList()
    {
        const string single = """{"Name":"USB Input Device","PNPDeviceID":"USB\\VID_046D&PID_C077\\5&21DC7979&0&9","Manufacturer":"x"}""";

        UsbHostService.ParseWindows(single).Should().ContainSingle().Which.Ids.Should().Be("046d:c077");
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""[{"Name":"No id"}]""")]
    [InlineData("""[{"Name":"x","PNPDeviceID":"USB\\ROOT_HUB30\\4&1&0"}]""")]
    public void Windows_NothingUsable_IsAnEmptyList(string json) => UsbHostService.ParseWindows(json).Should().BeEmpty();

    // ---- macOS: the layout system_profiler documents; this fixture was written by hand, not captured on a Mac ----

    private const string MacProfile = """
        {"SPUSBDataType":[{"_name":"USB31Bus","host_controller":"AppleT8103USBXHCI","_items":[
          {"_name":"USB Receiver","vendor_id":"0x046d  (Logitech Inc.)","product_id":"0xc52b","manufacturer":"Logitech"},
          {"_name":"Hub","vendor_id":"0x05e3","product_id":"0x0610","_items":[
            {"_name":"Keyboard","vendor_id":"apple_vendor_id","product_id":"0x0267"}]}]}]}
        """;

    [Fact]
    public void Mac_WalksNestedHubs_AndNamesAppleAsVendor05ac()
    {
        var devices = UsbHostService.ParseMac(MacProfile);

        devices.Select(d => d.Ids).Should().Equal("046d:c52b", "05e3:0610", "05ac:0267");
        devices[0].Name.Should().Be("USB Receiver");
    }

    [Fact]
    public void Mac_AnEmptyOrUnrelatedProfile_IsEmpty()
    {
        UsbHostService.ParseMac("").Should().BeEmpty();
        UsbHostService.ParseMac("""{"Other":[]}""").Should().BeEmpty();
    }

    // ---- this machine ----

    [WindowsOnlyFact]
    public async Task Windows_ListingThisComputer_WorksWhateverIsPluggedIn()
    {
        // A build server is a virtual machine and may have no USB devices at all, so the number of devices is
        // not asserted: only that PowerShell ran and parsed (an empty Note), and that what it found is sane.
        var sut = new UsbHostService(NullLogger<UsbHostService>.Instance);

        var listing = await sut.ListAsync();

        listing.Note.Should().BeEmpty();
        listing.Devices.Should().OnlyContain(d => d.VendorId != 0 && d.Name.Length > 0);
    }
}

public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Needs Windows";
    }
}
