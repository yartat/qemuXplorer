using System.Runtime.InteropServices;
using FluentAssertions;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class HostPlatformTests
{
    [Theory]
    [InlineData(Architecture.X64, GuestArchitecture.X86_64, true)]
    [InlineData(Architecture.X64, GuestArchitecture.I386, true)]
    [InlineData(Architecture.X64, GuestArchitecture.Aarch64, false)]
    [InlineData(Architecture.X64, GuestArchitecture.RiscV64, false)]
    [InlineData(Architecture.X86, GuestArchitecture.I386, true)]
    [InlineData(Architecture.X86, GuestArchitecture.X86_64, false)]
    [InlineData(Architecture.Arm64, GuestArchitecture.Aarch64, true)]
    [InlineData(Architecture.Arm64, GuestArchitecture.X86_64, false)]
    [InlineData(Architecture.Arm64, GuestArchitecture.Arm, false)]
    [InlineData(Architecture.Arm, GuestArchitecture.Arm, true)]
    [InlineData(Architecture.RiscV64, GuestArchitecture.RiscV64, true)]
    [InlineData(Architecture.RiscV64, GuestArchitecture.RiscV32, false)]
    [InlineData(Architecture.Ppc64le, GuestArchitecture.Ppc64, true)]
    [InlineData(Architecture.S390x, GuestArchitecture.S390x, true)]
    [InlineData(Architecture.Wasm, GuestArchitecture.X86_64, false)]
    [InlineData(Architecture.LoongArch64, GuestArchitecture.Mips64, false)]
    public void HostCpu_IsOnlyUsableWhenTheArchitecturesMatch(Architecture host, GuestArchitecture guest, bool expected)
    {
        HostPlatform.CanUseHostCpu(guest, host).Should().Be(expected);
    }

    [Fact]
    public void TheNoHostOverload_UsesTheOperatingSystemsArchitecture()
    {
        // The OS architecture, not the process's: an x64 build under emulation on Arm still sits
        // on an Arm CPU.
        foreach (var guest in Enum.GetValues<GuestArchitecture>())
            HostPlatform.CanUseHostCpu(guest).Should().Be(HostPlatform.CanUseHostCpu(guest, RuntimeInformation.OSArchitecture));
    }

    [Fact]
    public void ThisComputer_AgreesWithAtMostOneGuestFamily()
    {
        // Sanity: a host is never a match for guests of two unrelated families.
        var matches = Enum.GetValues<GuestArchitecture>().Where(a => HostPlatform.CanUseHostCpu(a)).ToList();

        matches.Count.Should().BeLessThanOrEqualTo(2, "only x64 maps to two guests (x86_64 and i386)");
    }
}
