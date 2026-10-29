using System.Runtime.InteropServices;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

/// <summary>What this computer is, as far as choosing a guest CPU is concerned.</summary>
public static class HostPlatform
{
    /// <summary>
    /// The architecture of the operating system, not of this process: an x64 build running under
    /// emulation on Arm still has an Arm host CPU, which is what <c>-cpu host</c> would pass through.
    /// </summary>
    public static Architecture Current => RuntimeInformation.OSArchitecture;

    public static bool CanUseHostCpu(GuestArchitecture guest) => CanUseHostCpu(guest, Current);

    /// <summary>
    /// True when <c>-cpu host</c> can make sense: the guest runs the same instruction set as the
    /// computer, so a hardware accelerator (KVM, HVF or WHPX) can run it directly. Across
    /// architectures QEMU emulates, and <c>host</c> fails ("unable to find CPU model 'host'").
    /// </summary>
    /// <remarks>
    /// Deliberately conservative: a 32-bit Arm guest on an Arm64 host is not counted, because it
    /// needs 32-bit support that most Arm64 cores no longer have. i386 on x64 is counted; a 64-bit
    /// x86 CPU runs 32-bit guests.
    /// </remarks>
    public static bool CanUseHostCpu(GuestArchitecture guest, Architecture host) => host switch
    {
        Architecture.X64 => guest is GuestArchitecture.X86_64 or GuestArchitecture.I386,
        Architecture.X86 => guest is GuestArchitecture.I386,
        Architecture.Arm64 => guest is GuestArchitecture.Aarch64,
        Architecture.Arm => guest is GuestArchitecture.Arm,
        Architecture.RiscV64 => guest is GuestArchitecture.RiscV64,
        Architecture.Ppc64le => guest is GuestArchitecture.Ppc64,
        Architecture.S390x => guest is GuestArchitecture.S390x,
        _ => false
    };
}
