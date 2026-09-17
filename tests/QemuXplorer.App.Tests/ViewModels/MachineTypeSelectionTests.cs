using Avalonia.Headless.XUnit;
using FluentAssertions;
using NSubstitute;
using QemuXplorer.App.ViewModels;
using QemuXplorer.Core.Services;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

/// <summary>
/// Covers the machine-type / architecture relation as the editor uses it: the picker lists only
/// what the selected architecture's emulator offers, and changing architecture cannot leave a
/// machine behind that the new emulator would refuse.
/// </summary>
public class MachineTypeSelectionTests
{
    private static VmEditViewModel CreateSut()
    {
        var vmRepo = Substitute.For<IVirtualMachineRepository>();
        var qemuRepo = Substitute.For<IQemuInstallationRepository>();
        var argBuilder = Substitute.For<IQemuArgumentBuilder>();
        var processManager = Substitute.For<IQemuProcessManager>();

        var service = new VirtualMachineService(vmRepo, qemuRepo, argBuilder, processManager);
        return new VmEditViewModel(service, argBuilder);
    }

    private static VmEditViewModel LoadedWith(GuestArchitecture arch, string machineType)
    {
        var sut = CreateSut();
        sut.LoadVm(new VirtualMachine { Architecture = arch, MachineType = machineType });
        return sut;
    }

    [AvaloniaFact]
    public void LoadVm_PopulatesMachineTypesForTheArchitecture()
    {
        var sut = LoadedWith(GuestArchitecture.X86_64, "q35");

        sut.MachineTypes.Should().NotBeEmpty();
        sut.MachineTypes.Should().OnlyContain(m => m.SupportsArchitecture(GuestArchitecture.X86_64));
    }

    [AvaloniaFact]
    public void MachineTypes_ExcludeMachinesFromOtherArchitectures()
    {
        var sut = LoadedWith(GuestArchitecture.X86_64, "q35");

        sut.MachineTypes.Select(m => m.QemuName).Should().NotContain("pseries");
    }

    [AvaloniaFact]
    public void ChangingArchitecture_RefiltersTheList()
    {
        var sut = LoadedWith(GuestArchitecture.X86_64, "q35");

        sut.Architecture = GuestArchitecture.Ppc64;

        sut.MachineTypes.Should().OnlyContain(m => m.SupportsArchitecture(GuestArchitecture.Ppc64));
        sut.MachineTypes.Select(m => m.QemuName).Should().Contain("pseries").And.NotContain("q35");
    }

    [AvaloniaFact]
    public void ChangingArchitecture_ReplacesAMachineTheNewEmulatorCannotRun()
    {
        var sut = LoadedWith(GuestArchitecture.X86_64, "q35");

        sut.Architecture = GuestArchitecture.S390x;

        sut.MachineType.Should().NotBe("q35");
        MachineCatalogSupports(sut.MachineType, GuestArchitecture.S390x).Should().BeTrue();
    }

    [AvaloniaFact]
    public void ChangingArchitecture_KeepsAMachineThatIsStillValid()
    {
        // virt is offered by aarch64, arm, riscv64 and riscv32, so switching between them
        // should not disturb the user's choice.
        var sut = LoadedWith(GuestArchitecture.Aarch64, "virt");

        sut.Architecture = GuestArchitecture.RiscV64;

        sut.MachineType.Should().Be("virt");
    }

    [AvaloniaFact]
    public void SelectedMachineType_WritesTheQemuNameThrough()
    {
        var sut = LoadedWith(GuestArchitecture.X86_64, "q35");

        sut.SelectedMachineType = sut.MachineTypes.First(m => m.QemuName == "microvm");

        sut.MachineType.Should().Be("microvm");
    }

    [AvaloniaFact]
    public void LoadVm_DoesNotRewriteAStoredMachineItCannotVouchFor()
    {
        // A versioned variant is valid for QEMU but absent from the catalog. Opening the editor
        // must not silently replace what the user saved.
        var sut = LoadedWith(GuestArchitecture.X86_64, "pc-q35-9.2");

        sut.MachineType.Should().Be("pc-q35-9.2");
        sut.SelectedMachineType.Should().BeNull();
        sut.HasUnknownMachineType.Should().BeTrue();
    }

    [AvaloniaFact]
    public void KnownMachine_RaisesNoWarning()
    {
        LoadedWith(GuestArchitecture.X86_64, "q35").HasUnknownMachineType.Should().BeFalse();
    }

    [AvaloniaFact]
    public void EveryArchitecture_YieldsANonEmptyPicker()
    {
        foreach (var arch in Enum.GetValues<GuestArchitecture>())
        {
            var sut = LoadedWith(GuestArchitecture.X86_64, "q35");
            sut.Architecture = arch;

            sut.MachineTypes.Should().NotBeEmpty($"the picker must offer something for {arch}");
            sut.HasUnknownMachineType.Should().BeFalse($"{arch} should land on a valid machine");
        }
    }

    private static bool MachineCatalogSupports(string name, GuestArchitecture arch)
        => QemuXplorer.Models.MachineCatalog.IsValidFor(name, arch);
}
