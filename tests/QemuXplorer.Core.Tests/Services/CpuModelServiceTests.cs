using FluentAssertions;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class CpuModelServiceTests
{
    // Trimmed from `qemu-system-x86_64 -cpu help` on QEMU 11.1.
    private const string X86 =
        "Available CPUs:\n"
        + "  486                   (alias configured by machine type)\n"
        + "  486-v1                \n"
        + "  Broadwell-IBRS        (alias of Broadwell-v3)\n"
        + "  Broadwell-v1          Intel Core Processor (Broadwell)\n"
        + "  host                  processor with all supported host features\n"
        + "  max                   Enables all features supported by the accelerator in the current host\n"
        + "\nRecognized CPUID flags:\n  3dnow 3dnowext\n";

    // aarch64 prints bare names with no description.
    private const string Aarch64 = "Available CPUs:\n  a64fx\n  cortex-a53\n  max\n";

    // s390x follows the model list with flag sections that must not leak in.
    private const string S390X =
        "Available CPUs:\n"
        + "  qemu            QEMU Virtual CPU version 2.5+       (migration-safe)\n"
        + "  z900-base       IBM zSeries 900 GA1                 (static, migration-safe)\n"
        + "\nRecognized feature flags:\n  aefsi\n  aen\n";

    [Fact]
    public void Parse_ReadsNamesAndDescriptions()
    {
        var models = CpuModelService.Parse(X86);

        models.Select(m => m.Name).Should().Equal("486", "486-v1", "Broadwell-IBRS", "Broadwell-v1", "host", "max");
        models.Single(m => m.Name == "Broadwell-v1").Description.Should().Be("Intel Core Processor (Broadwell)");
        models.Single(m => m.Name == "Broadwell-IBRS").Description.Should().Be("(alias of Broadwell-v3)");
        models.Single(m => m.Name == "486-v1").Description.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AcceptsBareNames()
    {
        CpuModelService.Parse(Aarch64).Select(m => m.Name).Should().Equal("a64fx", "cortex-a53", "max");
    }

    [Fact]
    public void Parse_StopsAtTheBlankLineSoFlagSectionsAreNotModels()
    {
        var names = CpuModelService.Parse(S390X).Select(m => m.Name);

        names.Should().Equal("qemu", "z900-base");
        names.Should().NotContain(["aefsi", "aen", "3dnow"]);
    }

    [Fact]
    public void Parse_StripsTheLegacyX86Prefix()
    {
        CpuModelService.Parse("Available CPUs:\n  x86 Broadwell  Intel Core\n")
            .Should().ContainSingle().Which.Name.Should().Be("Broadwell");
    }

    [Fact]
    public void Parse_IgnoresRepeatedNames()
    {
        CpuModelService.Parse("Available CPUs:\n  max\n  max\n").Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("qemu-system-x86_64: error: something unrelated\n")]
    public void Parse_OutputWithoutAModelList_YieldsNothing(string text)
    {
        CpuModelService.Parse(text).Should().BeEmpty();
    }

    [Fact]
    public async Task GetModels_PutsHostFirst_EvenWhenQemuDoesNotListIt()
    {
        var sut = new FakeCpuModelService(Aarch64);

        var models = await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);

        models.First().Name.Should().Be("host");
        models[1].Should().BeSameAs(CpuModelService.MachineDefault, "the machine default comes right after host");
        models.Select(m => m.Name).Should().Equal("host", "", "a64fx", "cortex-a53", "max");
    }

    [Theory]
    [InlineData(GuestArchitecture.X86_64, System.Runtime.InteropServices.Architecture.X64, "host")]
    [InlineData(GuestArchitecture.I386, System.Runtime.InteropServices.Architecture.X64, "host")]
    [InlineData(GuestArchitecture.Aarch64, System.Runtime.InteropServices.Architecture.Arm64, "host")]
    [InlineData(GuestArchitecture.Aarch64, System.Runtime.InteropServices.Architecture.X64, "")]
    [InlineData(GuestArchitecture.X86_64, System.Runtime.InteropServices.Architecture.Arm64, "")]
    [InlineData(GuestArchitecture.RiscV64, System.Runtime.InteropServices.Architecture.X64, "")]
    public void DefaultModel_IsHostOnlyWhenTheArchitecturesMatch(
        GuestArchitecture guest, System.Runtime.InteropServices.Architecture host, string expected)
    {
        CpuModelService.DefaultModelName(guest, host).Should().Be(expected);
    }

    [Fact]
    public async Task GetModels_NeverListsTheMachineDefaultTwice_OrAnEmptyNameFromQemu()
    {
        var sut = new FakeCpuModelService("Available CPUs:\n  max\n");

        var models = await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);

        models.Count(m => m.Name.Length == 0).Should().Be(1);
    }

    [Fact]
    public async Task GetModels_ReplacesQemusOwnHostEntryRatherThanDuplicatingIt()
    {
        var sut = new FakeCpuModelService(X86);

        var models = await sut.GetModelsAsync("/q", GuestArchitecture.X86_64);

        models.Count(m => m.Name == "host").Should().Be(1);
        models[0].Should().BeSameAs(CpuModelService.Host);
    }

    [Fact]
    public async Task GetModels_FillsInDescriptionsQemuLeavesOut()
    {
        // aarch64 prints bare names; the descriptions must still reach the editor's picker.
        var sut = new FakeCpuModelService(Aarch64);

        var models = await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);

        models.Single(m => m.Name == "a64fx").Description.Should().Contain("A64FX");
        models.Where(m => m.Name != "max").Should().OnlyContain(m => m.Description.Length > 0);
    }

    [Fact]
    public async Task GetModels_AsksQemuOncePerBinary()
    {
        var sut = new FakeCpuModelService(Aarch64);

        await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);
        await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);
        await sut.GetModelsAsync("/q", GuestArchitecture.X86_64);

        sut.Calls.Should().HaveCount(2, "aarch64 is cached, x86_64 is a different binary");
    }

    [Fact]
    public async Task GetModels_RunsTheBinaryForTheRequestedArchitecture()
    {
        var sut = new FakeCpuModelService(Aarch64);

        await sut.GetModelsAsync("/q", GuestArchitecture.RiscV64);

        sut.Calls.Single().Should().EndWith(QemuArgumentBuilder.GetBinaryName(GuestArchitecture.RiscV64));
    }

    [Fact]
    public async Task GetModels_AFailureIsNotCached_SoALaterCallCanSucceed()
    {
        var sut = new FakeCpuModelService(Aarch64) { Fail = true };

        var act = () => sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);
        await act.Should().ThrowAsync<InvalidOperationException>();

        sut.Fail = false;
        var models = await sut.GetModelsAsync("/q", GuestArchitecture.Aarch64);

        models.Should().Contain(m => m.Name == "a64fx");
    }

    private sealed class FakeCpuModelService(string output) : CpuModelService
    {
        public List<string> Calls { get; } = [];
        public bool Fail { get; set; }

        protected override Task<string> ReadCpuHelpAsync(string exe, CancellationToken ct)
        {
            Calls.Add(exe);
            return Fail
                ? Task.FromException<string>(new InvalidOperationException("qemu is gone"))
                : Task.FromResult(output);
        }
    }
}
