using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.App.ViewModels;
using QemuXplorer.App.Views;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

public class UsbDeviceViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-usbvm-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static UsbDeviceViewModel Adding()
    {
        var sut = new UsbDeviceViewModel(new UsbHostService(NullLogger<UsbHostService>.Instance));
        sut.InitializeForAdd();
        return sut;
    }

    private static UsbDeviceViewModel Editing(UsbDevice device)
    {
        var sut = new UsbDeviceViewModel(new UsbHostService(NullLogger<UsbHostService>.Instance));
        sut.InitializeForEdit(device);
        return sut;
    }

    private static UsbTypeChoice Choice(UsbDeviceType type) => UsbDeviceViewModel.Types.First(t => t.Type == type);

    /// <summary>An adder already on the settings step of a type that does not list host devices.</summary>
    private static UsbDeviceViewModel AddingOf(UsbDeviceType type)
    {
        var sut = Adding();
        sut.SelectedType = Choice(type);
        sut.NextCommand.Execute(null);
        return sut;
    }

    // ---- the wizard ----

    [AvaloniaFact]
    public void Adding_StartsOnTheTypeStep_OfferingEveryType()
    {
        var sut = Adding();

        sut.IsStep0.Should().BeTrue();
        sut.TypeChoices.Select(t => t.Type).Should().BeEquivalentTo(Enum.GetValues<UsbDeviceType>());
        sut.StepTitle.Should().Contain("Step 1 of 3");
    }

    [AvaloniaFact]
    public void TheWizard_WalksTypeSettingsReview_AndBack()
    {
        var sut = AddingOf(UsbDeviceType.Tablet);

        sut.IsStep1.Should().BeTrue();
        sut.NextCommand.Execute(null);
        sut.IsStep2.Should().BeTrue();
        sut.IsLastStep.Should().BeTrue();
        sut.CommandPreview.Should().Be("-usb -device usb-tablet");
        sut.BackCommand.Execute(null);
        sut.IsStep1.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Finishing_ReturnsTheDeviceWithItsTypeAndName()
    {
        var sut = AddingOf(UsbDeviceType.Keyboard);
        sut.Description = "Typing";
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;

        sut.FinishCommand.Execute(null);

        closed.Should().BeTrue();
        (sut.Result!.DeviceType, sut.Result.Description, sut.Result.IsPassthrough).Should().Be((UsbDeviceType.Keyboard, "Typing", true));
    }

    [AvaloniaFact]
    public void Cancel_ReturnsNothing()
    {
        var sut = Adding();

        sut.CancelCommand.Execute(null);

        sut.Result.Should().BeNull();
    }

    // ---- host passthrough ----

    [AvaloniaFact]
    public void Host_NeedsHexIds_AndAcceptsA0xPrefix()
    {
        var sut = AddingOf(UsbDeviceType.HostPassthrough);
        sut.ValidationMessage.Should().Contain("vendor");

        sut.VendorIdText = "0x046D";
        sut.ProductIdText = "zzzz";
        sut.ValidationMessage.Should().Contain("product");

        sut.ProductIdText = "c52b";
        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("usb-host,vendorid=0x046d,productid=0xc52b");
    }

    [AvaloniaFact]
    public void Host_ChoosingFromTheList_FillsTheIdsAndTheName()
    {
        var sut = Adding();

        sut.SelectedHostDevice = new HostUsbDevice(0x046d, 0xc52b, "USB Receiver", "Logitech", 1, 7);

        (sut.VendorIdText, sut.ProductIdText, sut.Description).Should().Be(("046d", "c52b", "USB Receiver"));
        (sut.HostBus, sut.HostAddress).Should().Be((1, 7));
    }

    [AvaloniaFact]
    public void Host_ByPort_NeedsBusAndAddress_AndIgnoresTheIds()
    {
        var sut = AddingOf(UsbDeviceType.HostPassthrough);
        sut.UseBusAndAddress = true;
        sut.ByPort.Should().BeTrue();
        sut.ValidationMessage.Should().Contain("bus");

        sut.HostBus = 2;
        sut.ValidationMessage.Should().Contain("address");

        sut.HostAddress = 5;
        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("usb-host,hostbus=2,hostaddr=5");
    }

    [AvaloniaFact]
    public void Host_SaysWhatThisOperatingSystemNeeds()
    {
        var sut = Adding();

        sut.HasHostNote.Should().BeTrue();
        sut.HostNote.Should().Contain(OperatingSystem.IsWindows() ? "WinUSB" : OperatingSystem.IsMacOS() ? "privileges" : "udev");
    }

    // ---- storage ----

    [AvaloniaFact]
    public void Storage_NeedsAnExistingImage_AndPassesItsSettings()
    {
        var sut = AddingOf(UsbDeviceType.Storage);
        sut.ValidationMessage.Should().Contain("image file");

        sut.ImagePath = Path.Combine(_dir, "missing.img");
        sut.ValidationMessage.Should().Contain("does not exist");

        var image = Path.Combine(_dir, "stick.img");
        File.WriteAllText(image, "x");
        sut.ImagePath = image;
        sut.IsRemovable = true;
        sut.IsBoot = true;

        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("usb-storage,drive=usbdrive0,removable=on,bootindex=1");
        sut.FinishCommand.Execute(null);
        (sut.Result!.ImagePath, sut.Result.IsRemovable, sut.Result.IsBootDevice).Should().Be((image, true, true));
    }

    [AvaloniaFact]
    public void Storage_AReadOnlyFormat_ForcesReadOnly()
    {
        var sut = AddingOf(UsbDeviceType.Storage);

        sut.ImageFormat = sut.ImageFormats.First(f => f.Format == DiskFormat.Dmg);

        sut.IsReadOnly.Should().BeTrue();
        sut.CanEditReadOnly.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Storage_EveryFormatHasADescription_AndItFollowsTheSelection()
    {
        var sut = AddingOf(UsbDeviceType.Storage);

        sut.ImageFormats.Should().OnlyContain(f => f.Description.Length > 0);
        sut.ImageFormatDescription.Should().Be(sut.ImageFormat!.Description);

        sut.ImageFormat = sut.ImageFormats.First(f => f.Format == DiskFormat.QCow2);

        sut.ImageFormatDescription.Should().Contain("snapshots");
        sut.ImageFormatDescription.Should().Be(sut.ImageFormats.First(f => f.Format == DiskFormat.QCow2).Description);
    }

    // ---- the other types ----

    [AvaloniaTheory]
    [InlineData(UsbDeviceType.Tablet)]
    [InlineData(UsbDeviceType.Mouse)]
    [InlineData(UsbDeviceType.Keyboard)]
    public void InputDevices_HaveNoSettings_AndCannotBoot(UsbDeviceType type)
    {
        var sut = AddingOf(type);

        sut.HasNoSettings.Should().BeTrue();
        sut.CanBoot.Should().BeFalse();
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public void Audio_TheDescriptionFollowsTheSelectedDriver()
    {
        var sut = AddingOf(UsbDeviceType.Audio);
        sut.AudioDriverDescription.Should().Be(sut.SelectedAudioDriver.Description);

        sut.SelectedAudioDriver = sut.AudioDriverChoices.First(d => d.Name == "wav");

        sut.AudioDriver.Should().Be("wav");
        sut.AudioDriverDescription.Should().Contain("file");
        sut.AudioDriverChoices.Should().OnlyContain(d => d.Description.Length > 0);
    }

    [AvaloniaFact]
    public void Audio_ADriverSavedButNotOffered_IsAddedToTheListWhenEditing()
    {
        var sut = Editing(new UsbDevice { DeviceType = UsbDeviceType.Audio, AudioDriver = "oss" });

        sut.AudioDriver.Should().Be("oss");
        sut.AudioDriverChoices.Select(d => d.Name).Should().Contain("oss");
        sut.SelectedAudioDriver.Name.Should().Be("oss");
    }

    [AvaloniaFact]
    public void Controller_OffersEveryModel_AndTheDescriptionFollowsTheSelection()
    {
        var sut = AddingOf(UsbDeviceType.Controller);

        sut.ControllerChoices.Select(c => c.Model).Should().BeEquivalentTo(Enum.GetValues<UsbControllerModel>());
        sut.ControllerDescription.Should().Contain("USB 3");

        sut.SelectedController = sut.ControllerChoices.First(c => c.Model == UsbControllerModel.Uhci);

        sut.Controller.Should().Be(UsbControllerModel.Uhci);
        sut.ControllerDescription.Should().Contain("1.1");
        Editing(new UsbDevice { DeviceType = UsbDeviceType.Controller, Controller = UsbControllerModel.Ehci })
            .ControllerDescription.Should().Contain("USB 2");
    }

    [AvaloniaFact]
    public void Audio_OffersTheDriversOfThisSystem_AndWarnsAboutNone()
    {
        var sut = AddingOf(UsbDeviceType.Audio);

        sut.AudioDrivers.Should().Contain("none").And.Contain(OperatingSystem.IsWindows() ? "dsound" : OperatingSystem.IsMacOS() ? "coreaudio" : "pa");
        sut.AudioDriver.Should().Be(sut.AudioDrivers[0]);
        sut.HasWarning.Should().BeFalse();

        sut.AudioDriver = "none";
        sut.HasWarning.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Serial_EachDestinationAsksForWhatItNeeds()
    {
        var sut = AddingOf(UsbDeviceType.Serial);
        sut.ValidationMessage.Should().BeNull();

        sut.SerialBackend = UsbSerialBackend.File;
        sut.SerialIsFile.Should().BeTrue();
        sut.ValidationMessage.Should().Contain("file");
        sut.SerialTarget = Path.Combine(_dir, "out.log");
        sut.ValidationMessage.Should().BeNull();

        sut.SerialBackend = UsbSerialBackend.Tcp;
        sut.SerialIsTcp.Should().BeTrue();
        sut.SerialPort = 0;
        sut.ValidationMessage.Should().Contain("port");
        sut.SerialPort = 4555;
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public void SmartCard_DefaultsToThePortVscclientUses_AndKeepsOnlyThatPort()
    {
        var sut = AddingOf(UsbDeviceType.SmartCard);

        sut.SerialPort.Should().Be(2001);
        sut.HasWarning.Should().BeTrue();
        sut.CommandPreview.Should().Contain("port=2001").And.Contain("ccid-card-passthru");
    }

    [AvaloniaFact]
    public void Controller_ReplacesTheDefault_AndWarnsAboutPci()
    {
        var sut = AddingOf(UsbDeviceType.Controller);
        sut.Controller = UsbControllerModel.Ehci;

        sut.HasWarning.Should().BeTrue();
        sut.CommandPreview.Should().Be("-device usb-ehci");
    }

    [AvaloniaFact]
    public void AnUnattachedEntry_IsStillSaved_ButSaysSo()
    {
        var sut = AddingOf(UsbDeviceType.Mouse);
        sut.IsAttached = false;

        sut.Summary.Should().Contain("not attached");
        sut.FinishCommand.Execute(null);
        sut.Result!.IsPassthrough.Should().BeFalse();
    }

    // ---- editing ----

    [AvaloniaFact]
    public void Editing_OpensOnTheSettingsOfTheDevicesOwnType_AndCannotChangeIt()
    {
        var device = new UsbDevice { DeviceType = UsbDeviceType.Serial, SerialBackend = UsbSerialBackend.Tcp, SerialPort = 5000, Description = "Console" };

        var sut = Editing(device);

        sut.IsEditing.Should().BeTrue();
        sut.IsStep1.Should().BeTrue();
        (sut.IsSerial, sut.IsHost, sut.IsStorage, sut.IsAudio).Should().Be((true, false, false, false));
        sut.CanEditType.Should().BeFalse();
        sut.CanGoBack.Should().BeFalse();
        sut.ShowNext.Should().BeFalse();
        (sut.SerialBackend, sut.SerialPort, sut.Description).Should().Be((UsbSerialBackend.Tcp, 5000, "Console"));
        sut.DialogTitle.Should().StartWith("Edit USB device");
    }

    [AvaloniaFact]
    public void Editing_AHostDevice_ShowsItsIds_AndSavingKeepsItsIdentity()
    {
        var device = new UsbDevice { VmId = Guid.NewGuid(), VendorId = 0x046d, ProductId = 0xc52b, IsBootDevice = true };
        var sut = Editing(device);
        (sut.VendorIdText, sut.ProductIdText, sut.IsBoot).Should().Be(("046d", "c52b", true));

        sut.ProductIdText = "c534";
        sut.FinishCommand.Execute(null);

        var result = sut.Result!;
        (result.Id, result.VmId, result.ProductId, result.VendorId).Should().Be((device.Id, device.VmId, (ushort)0xc534, (ushort)0x046d));
        device.ProductId.Should().Be(0xc52b, "the original is not touched");
    }

    [AvaloniaFact]
    public void Editing_AnOldRow_ReadsAsAHostDevice()
    {
        // Rows saved before types existed have DeviceType 0 and a vendor and product.
        var sut = Editing(new UsbDevice { VendorId = 0x1234, ProductId = 0x5678, Description = "Old" });

        sut.IsHost.Should().BeTrue();
        sut.SelectedType.Type.Should().Be(UsbDeviceType.HostPassthrough);
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(UsbDeviceType.HostPassthrough)]
    [InlineData(UsbDeviceType.Storage)]
    [InlineData(UsbDeviceType.Audio)]
    [InlineData(UsbDeviceType.Serial)]
    [InlineData(UsbDeviceType.SmartCard)]
    [InlineData(UsbDeviceType.Controller)]
    public void Editing_ShowsOnlyTheSettingsOfThatType(UsbDeviceType type)
    {
        var sut = Editing(new UsbDevice { DeviceType = type });

        new[] { sut.IsHost, sut.IsStorage, sut.IsAudio, sut.IsSerial, sut.IsSmartCard, sut.IsController }.Count(shown => shown).Should().Be(1);
    }

    // ---- the window ----

    [AvaloniaFact]
    public void TheWindow_BindsInEveryStepAndType()
    {
        var sut = Adding();
        var window = new UsbDeviceView { DataContext = sut };
        window.Show();

        foreach (var type in UsbDeviceViewModel.Types.Where(t => t.Type != UsbDeviceType.HostPassthrough))
        {
            sut.SelectedType = type;
            sut.Step = 0;
            sut.NextCommand.Execute(null);
            sut.NextCommand.Execute(null);
            sut.BackCommand.Execute(null);
            sut.BackCommand.Execute(null);
        }

        window.IsVisible.Should().BeTrue();
        window.Close();
    }
}
