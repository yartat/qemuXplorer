using FluentAssertions;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class CpuModelDescriptionsTests
{
    public static TheoryData<GuestArchitecture> Architectures
    {
        get
        {
            var data = new TheoryData<GuestArchitecture>();
            foreach (var arch in Enum.GetValues<GuestArchitecture>())
                data.Add(arch);
            return data;
        }
    }

    /// <summary>The "Available CPUs" block of a real `qemu-system-&lt;arch&gt; -cpu help` on QEMU 11.1.</summary>
    private static string Fixture(GuestArchitecture arch) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "cpu-help", arch.ToString().ToLowerInvariant() + ".txt"));

    private static IReadOnlyList<CpuModelInfo> Enriched(GuestArchitecture arch) =>
        CpuModelDescriptions.Enrich(CpuModelService.Parse(Fixture(arch)), arch);

    private static string DescriptionOf(GuestArchitecture arch, string name) =>
        Enriched(arch).Single(m => m.Name == name).Description;

    [Theory]
    [MemberData(nameof(Architectures))]
    public void EveryModelOfARealQemu_HasADescription(GuestArchitecture arch)
    {
        var models = Enriched(arch);

        models.Should().NotBeEmpty();
        models.Where(m => m.Description.Length == 0).Select(m => m.Name)
            .Should().BeEmpty($"{arch} models without a description");
    }

    [Theory]
    [MemberData(nameof(Architectures))]
    public void NoDescriptionIsJustAPvr_OrABareAliasMarker(GuestArchitecture arch)
    {
        // These are the shapes QEMU prints that tell the user nothing.
        Enriched(arch).Select(m => m.Description)
            .Should().NotContain(d => d.StartsWith("PVR ") || d.StartsWith("(alias"));
    }

    [Fact]
    public void ADescriptionQemuSupplies_IsKeptAsIs()
    {
        DescriptionOf(GuestArchitecture.X86_64, "Broadwell-v1").Should().Be("Intel Core Processor (Broadwell)");
        DescriptionOf(GuestArchitecture.S390x, "qemu").Should().Be(
            CpuModelService.Parse(Fixture(GuestArchitecture.S390x)).Single(m => m.Name == "qemu").Description);
    }

    [Theory]
    [MemberData(nameof(Architectures))]
    public void Descriptions_HaveNoRunsOfPadding(GuestArchitecture arch)
    {
        // s390x aligns its columns with spaces inside the description.
        Enriched(arch).Select(m => m.Description).Should().NotContain(d => d.Contains("  "));
    }

    [Fact]
    public void AnAlias_TakesTheDescriptionOfItsTarget()
    {
        var text = DescriptionOf(GuestArchitecture.X86_64, "Broadwell-IBRS");

        text.Should().StartWith("Alias of Broadwell-v3: ");
        text.Should().Contain("Broadwell");
    }

    [Fact]
    public void AMachineTypeAlias_TakesTheDescriptionOfItsV1_AndSaysTheVersionVaries()
    {
        var text = DescriptionOf(GuestArchitecture.X86_64, "Broadwell");

        text.Should().Contain("Intel Core Processor (Broadwell)");
        text.Should().Contain("set by the machine type");
    }

    [Theory]
    [InlineData("486-v1", "Intel 486")]
    [InlineData("pentium-v1", "Intel Pentium")]
    [InlineData("pentium2-v1", "Intel Pentium II")]
    [InlineData("pentium3-v1", "Intel Pentium III")]
    public void TheOldestX86Models_AreDescribedByHand(string name, string expected)
    {
        DescriptionOf(GuestArchitecture.X86_64, name).Should().StartWith(expected);
    }

    [Fact]
    public void ArmCores_AreDescribedWithTheirArchitectureLevel()
    {
        DescriptionOf(GuestArchitecture.Aarch64, "cortex-a53").Should().Contain("Cortex-A53").And.Contain("Armv8-A");
        DescriptionOf(GuestArchitecture.Arm, "cortex-m4").Should().Contain("Cortex-M4").And.Contain("Armv7E-M");
    }

    [Fact]
    public void Mips_FollowsTheQemuDocumentation()
    {
        DescriptionOf(GuestArchitecture.Mips, "34Kf").Should().Contain("MIPS32 Release 2").And.Contain("2006");
        DescriptionOf(GuestArchitecture.Mips64, "R4000").Should().Contain("MIPS III").And.Contain("1991");
        DescriptionOf(GuestArchitecture.Mips64, "Loongson-3A4000").Should().Contain("2018");
    }

    [Fact]
    public void I7200_IsNanoMips_NotMips32Release6()
    {
        // Regression: an earlier hand-written table called it "MIPS32 Release 6". The QEMU CPU
        // models page lists it under nanoMIPS.
        DescriptionOf(GuestArchitecture.Mips, "I7200").Should().Contain("nanoMIPS").And.NotContain("Release 6");
    }

    [Fact]
    public void PowerPcPvrEntries_GetAFamilyButKeepTheirPvr()
    {
        var text = DescriptionOf(GuestArchitecture.Ppc64, "603");

        text.Should().StartWith("PowerPC 603");
        text.Should().Contain("PVR 00030100");
    }

    [Fact]
    public void PowerPcAlias_PointsAtItsTargetAndCarriesItsDescription()
    {
        DescriptionOf(GuestArchitecture.Ppc64, "mpc8240").Should().StartWith("Alias of 603: PowerPC 603");
    }

    [Theory]
    [InlineData("e500v2_v30", "version 3.0")]
    [InlineData("mpc8548_v21", "version 2.1")]
    [InlineData("7447a_v1.2", "version 1.2")]
    public void PowerPcRevisions_AreWrittenAsMajorDotMinor(string name, string expected)
    {
        DescriptionOf(GuestArchitecture.Ppc64, name).Should().Contain(expected);
    }

    [Fact]
    public void IbmPowerCores_AreNamedWithTheirRevision()
    {
        var name = Enriched(GuestArchitecture.Ppc64).First(m => m.Name.StartsWith("power9_")).Description;

        name.Should().StartWith("IBM POWER9").And.Contain("version");
    }

    [Fact]
    public void PowerFamilies_CoverEveryIdentifierShapeInTheFixture()
    {
        // The rules are patterns, so a new family in a newer QEMU would fall through. This proves
        // none does today and that each rule yields a human label rather than the raw PVR.
        Enriched(GuestArchitecture.Ppc64).Should().OnlyContain(m => !m.Description.StartsWith("PVR"));
    }

    [Fact]
    public void AnAliasCycle_DoesNotHang()
    {
        var models = new List<CpuModelInfo> { new("a", "(alias of b)"), new("b", "(alias of a)") };

        var act = () => CpuModelDescriptions.Enrich(models, GuestArchitecture.X86_64);

        act.Should().NotThrow();
    }

    [Fact]
    public void ACpuAddedByANewerQemu_StaysListed_WithoutInventedText()
    {
        var models = new List<CpuModelInfo> { new("cortex-z999") };

        var enriched = CpuModelDescriptions.Enrich(models, GuestArchitecture.Aarch64);

        enriched.Should().ContainSingle().Which.Description.Should().BeEmpty();
    }

    [Fact]
    public void Host_IsDescribedAsThePassthroughQemuRecommends()
    {
        CpuModelService.Host.Description.Should().Contain("Recommended").And.Contain("live migration");
    }
}
