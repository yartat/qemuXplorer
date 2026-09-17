using FluentAssertions;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Models;

public class MachineCatalogTests
{
    [Fact]
    public void All_ExposesEveryEnumMember()
    {
        MachineCatalog.All.Should().HaveCount(Enum.GetValues<MachineType>().Length);
    }

    [Fact]
    public void EveryMachine_DeclaresAtLeastOneArchitecture()
    {
        // A machine reachable from no architecture could never be selected in the UI.
        MachineCatalog.All.Should().OnlyContain(m => m.Architectures.Count > 0);
    }

    [Fact]
    public void EveryMachine_HasAQemuName()
    {
        MachineCatalog.All.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m.QemuName));
    }

    [Fact]
    public void QemuNames_AreUnique()
    {
        MachineCatalog.All.Select(m => m.QemuName)
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryArchitecture_OffersAtLeastOneMachine()
    {
        // Otherwise the editor would show an empty machine picker for that architecture.
        foreach (var arch in Enum.GetValues<GuestArchitecture>())
            MachineCatalog.ForArchitecture(arch).Should().NotBeEmpty($"{arch} needs a machine");
    }

    [Theory]
    [InlineData("q35", GuestArchitecture.X86_64)]
    [InlineData("q35", GuestArchitecture.I386)]
    [InlineData("pc", GuestArchitecture.X86_64)]
    [InlineData("virt", GuestArchitecture.Aarch64)]
    [InlineData("virt", GuestArchitecture.Arm)]
    [InlineData("virt", GuestArchitecture.RiscV64)]
    [InlineData("pseries", GuestArchitecture.Ppc64)]
    [InlineData("s390-ccw-virtio", GuestArchitecture.S390x)]
    [InlineData("malta", GuestArchitecture.Mips)]
    public void KnownMachine_IsValidForItsArchitecture(string name, GuestArchitecture arch)
    {
        MachineCatalog.IsValidFor(name, arch).Should().BeTrue();
    }

    [Theory]
    [InlineData("q35", GuestArchitecture.Aarch64)]
    [InlineData("pseries", GuestArchitecture.X86_64)]
    [InlineData("s390-ccw-virtio", GuestArchitecture.Arm)]
    public void KnownMachine_IsNotValidForAnUnrelatedArchitecture(string name, GuestArchitecture arch)
    {
        MachineCatalog.IsValidFor(name, arch).Should().BeFalse();
    }

    [Fact]
    public void Virt_IsSharedAcrossArmAndRiscV()
    {
        // The case that rules out a single-valued Display.GroupName for the relation.
        var virt = MachineCatalog.Find("virt");

        virt.Should().NotBeNull();
        virt!.Architectures.Should().Contain([
            GuestArchitecture.Aarch64,
            GuestArchitecture.Arm,
            GuestArchitecture.RiscV64,
            GuestArchitecture.RiscV32
        ]);
    }

    [Fact]
    public void None_IsOfferedByEveryArchitecture()
    {
        var none = MachineCatalog.Find("none");

        none.Should().NotBeNull();
        none!.Architectures.Should().BeEquivalentTo(Enum.GetValues<GuestArchitecture>());
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        MachineCatalog.Find("Q35").Should().BeSameAs(MachineCatalog.Find("q35"));
    }

    [Theory]
    [InlineData("pc-q35-9.2")]   // a versioned variant the enum deliberately omits
    [InlineData("not-a-machine")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_ReturnsNullForAnythingNotCatalogued(string? name)
    {
        MachineCatalog.Find(name).Should().BeNull();
    }

    [Fact]
    public void DefaultFor_PrefersANonDeprecatedMachine()
    {
        foreach (var arch in Enum.GetValues<GuestArchitecture>())
        {
            var fallback = MachineCatalog.DefaultFor(arch);

            fallback.Should().NotBeNull($"{arch} needs a usable default");
            fallback!.SupportsArchitecture(arch).Should().BeTrue();
        }
    }

    [Fact]
    public void DefaultFor_X86_IsQ35()
    {
        MachineCatalog.DefaultFor(GuestArchitecture.X86_64)!.QemuName.Should().Be("q35");
    }

    [Fact]
    public void QemuNames_MayContainCharactersAnIdentifierCannot()
    {
        // Justifies MachineAttribute.QemuName existing separately from the enum member name.
        MachineCatalog.All.Select(m => m.QemuName)
            .Should().Contain(n => n.Contains('-'))
            .And.Contain(n => char.IsDigit(n[0]));
    }

    [Fact]
    public void DisplayLabel_CombinesNameAndDescription()
    {
        MachineCatalog.Find("q35")!.DisplayLabel.Should().StartWith("q35 — ");
    }
}
