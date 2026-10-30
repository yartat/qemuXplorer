using FluentAssertions;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Models;

public class DiskFormatCatalogTests
{
    [Fact]
    public void EveryEnumMember_HasACatalogEntry() =>
        DiskFormatCatalog.All.Select(f => f.Format).Should().BeEquivalentTo(Enum.GetValues<DiskFormat>());

    [Fact]
    public void QemuNames_AreUnique() => DiskFormatCatalog.All.Select(f => f.QemuName).Should().OnlyHaveUniqueItems();

    [Fact]
    public void ExistingMembers_KeepTheirStoredIntegerValues()
    {
        // Persisted as ints: reordering would silently re-label every saved disk.
        ((int)DiskFormat.QCow2).Should().Be(0);
        ((int)DiskFormat.Raw).Should().Be(1);
        ((int)DiskFormat.Vmdk).Should().Be(2);
        ((int)DiskFormat.Vhd).Should().Be(3);
        ((int)DiskFormat.Vhdx).Should().Be(4);
        ((int)DiskFormat.Vdi).Should().Be(5);
    }

    [Fact]
    public void Qcow2Preallocation_OnWindows_OffersOnlyWhatQemuCanDo()
    {
        // Real qemu-img on Windows fails with "Unsupported preallocation mode" for falloc and full.
        DiskFormatCatalog.Qcow2PreallocationModes(windows: true).Should().Equal("off", "metadata");
        DiskFormatCatalog.Qcow2PreallocationModes(windows: false).Should().Equal("off", "metadata", "falloc", "full");
    }

    [Fact]
    public void Qcow2PreallocationChoices_AreThePlatformsList()
    {
        var option = DiskFormatCatalog.Get(DiskFormat.QCow2).CreateOptions.Single(o => o.Key == "preallocation");

        option.Choices.Should().Equal(DiskFormatCatalog.Qcow2PreallocationModes(OperatingSystem.IsWindows()));
    }

    [Theory]
    [InlineData(DiskFormat.Bochs)]
    [InlineData(DiskFormat.Cloop)]
    [InlineData(DiskFormat.Dmg)]
    [InlineData(DiskFormat.Parallels)]
    public void AttachOnlyFormats_CannotBeCreatedAndAreReadOnly(DiskFormat format)
    {
        var info = DiskFormatCatalog.Get(format);

        info.CanCreate.Should().BeFalse();
        info.IsReadOnly.Should().BeTrue();
        DiskFormatCatalog.Creatable.Should().NotContain(info);
    }

    [Theory]
    [InlineData(DiskFormat.QCow2)]
    [InlineData(DiskFormat.Qcow)]
    [InlineData(DiskFormat.Qed)]
    [InlineData(DiskFormat.Raw)]
    [InlineData(DiskFormat.Vdi)]
    [InlineData(DiskFormat.Vmdk)]
    [InlineData(DiskFormat.Vhd)]
    [InlineData(DiskFormat.Vhdx)]
    [InlineData(DiskFormat.Luks)]
    public void CreatableFormats_CanBeCreatedAndWritten(DiskFormat format)
    {
        DiskFormatCatalog.Get(format).CanCreate.Should().BeTrue();
        DiskFormatCatalog.Get(format).IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void OnlyLuks_NeedsAKeySecret() =>
        DiskFormatCatalog.All.Where(f => f.NeedsKeySecret).Select(f => f.Format).Should().Equal(DiskFormat.Luks);

    [Theory]
    [InlineData("vpc", DiskFormat.Vhd)]
    [InlineData("vhd", DiskFormat.Vhd)]
    [InlineData("QCOW2", DiskFormat.QCow2)]
    [InlineData("parallels", DiskFormat.Parallels)]
    public void FromQemuName_MapsDriverNames(string name, DiskFormat expected) =>
        DiskFormatCatalog.FromQemuName(name).Should().Be(expected);

    [Fact]
    public void FromQemuName_UnknownIsNull() => DiskFormatCatalog.FromQemuName("nope").Should().BeNull();

    [Fact]
    public void Options_HaveUniqueKeysPerFormat()
    {
        foreach (var f in DiskFormatCatalog.All)
            f.CreateOptions.Select(o => o.Key).Should().OnlyHaveUniqueItems($"{f.QemuName} options");
    }

    [Fact]
    public void ChoiceOptions_ListTheirChoices()
    {
        foreach (var o in DiskFormatCatalog.All.SelectMany(f => f.CreateOptions).Where(o => o.Kind == ImageOptionKind.Choice))
            o.Choices.Should().NotBeNullOrEmpty($"{o.Key} is a choice");
    }
}
