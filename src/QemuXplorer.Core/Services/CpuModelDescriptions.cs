using System.Text.RegularExpressions;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

/// <summary>
/// Fills in the descriptions <c>-cpu help</c> leaves out. QEMU prints a description for x86 and
/// s390x only; aarch64, arm, RISC-V and MIPS list bare names, PowerPC lists only a PVR, and many
/// x86 entries are aliases.
/// </summary>
/// <remarks>
/// A description QEMU supplies is always kept. Gaps are filled in this order:
/// aliases take the description of the model they point to; x86 "configured by machine type"
/// aliases take the one of their <c>-v1</c> entry; bare names come from the tables below; PowerPC
/// names are described by rules over the family in the name. A model nothing matches keeps an
/// empty description, so a CPU added in a newer QEMU still appears, just without text.
///
/// Sources: the MIPS table follows https://www.qemu.org/docs/master/system/qemu-cpu-models.html
/// (release level and year per core). The Arm, RISC-V and PowerPC entries are written from the
/// vendors' own naming and have NOT been checked against QEMU documentation, which does not cover
/// them on that page. A wrong line breaks nothing but misleads, so say no more than the core and
/// its architecture level, and keep them factual.
/// </remarks>
public static class CpuModelDescriptions
{
    private enum Family { X86, Arm, RiscV, Ppc, Mips, Other }

    private static readonly Regex AliasOf = new(@"^\(alias (?:of|for) (?<target>.+)\)$", RegexOptions.Compiled);
    private static readonly Regex Revision = new(@"_(?:v|dd)(?<rev>[0-9][0-9.]*[a-z]?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string MachineAlias = "(alias configured by machine type)";

    public static IReadOnlyList<CpuModelInfo> Enrich(IReadOnlyList<CpuModelInfo> models, GuestArchitecture architecture)
    {
        var family = FamilyOf(architecture);
        var raw = models.ToDictionary(m => m.Name, m => m.Description, StringComparer.Ordinal);
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        var inProgress = new HashSet<string>(StringComparer.Ordinal);

        string Resolve(string name)
        {
            if (resolved.TryGetValue(name, out var done))
                return done;
            if (!raw.TryGetValue(name, out var text))
                return string.Empty;
            // An alias loop would recurse forever; fall back to the text QEMU gave.
            if (!inProgress.Add(name))
                return text;

            string result;
            var alias = AliasOf.Match(text);
            if (alias.Success)
            {
                var target = alias.Groups["target"].Value.Trim();
                var targetText = Resolve(target);
                result = targetText.Length > 0 ? $"Alias of {target}: {targetText}" : $"Alias of {target}";
            }
            else if (text == MachineAlias)
            {
                var baseText = Resolve(name + "-v1");
                result = baseText.Length > 0
                    ? $"{baseText}; the exact version is set by the machine type"
                    : "Alias; the exact version is set by the machine type";
            }
            else if (text.Length == 0 || text.StartsWith("PVR", StringComparison.Ordinal))
            {
                result = Curated(family, name, text);
            }
            else
            {
                result = text;
            }

            inProgress.Remove(name);
            resolved[name] = result;
            return result;
        }

        return models.Select(m => m with { Description = Resolve(m.Name) }).ToList();
    }

    private static Family FamilyOf(GuestArchitecture architecture) => architecture switch
    {
        GuestArchitecture.X86_64 or GuestArchitecture.I386 => Family.X86,
        GuestArchitecture.Aarch64 or GuestArchitecture.Arm => Family.Arm,
        GuestArchitecture.RiscV64 or GuestArchitecture.RiscV32 => Family.RiscV,
        GuestArchitecture.Ppc64 or GuestArchitecture.Ppc => Family.Ppc,
        GuestArchitecture.Mips or GuestArchitecture.Mips64 => Family.Mips,
        _ => Family.Other
    };

    private static string Curated(Family family, string name, string qemuText)
    {
        var table = family switch
        {
            Family.X86 => X86,
            Family.Arm => Arm,
            Family.RiscV => RiscV,
            Family.Mips => Mips,
            _ => null
        };

        if (table is not null && table.TryGetValue(name, out var text))
            return text;

        return family == Family.Ppc ? Ppc(name, qemuText) : qemuText;
    }

    // QEMU prints no description for these four. The CPU models page groups them as "very old x86
    // CPU models, mostly predating the introduction of hardware assisted virtualization".
    private static readonly IReadOnlyDictionary<string, string> X86 = new Dictionary<string, string>
    {
        ["486-v1"] = "Intel 486; very old, predates hardware-assisted virtualization",
        ["pentium-v1"] = "Intel Pentium; very old, predates hardware-assisted virtualization",
        ["pentium2-v1"] = "Intel Pentium II; very old, predates hardware-assisted virtualization",
        ["pentium3-v1"] = "Intel Pentium III; very old, predates hardware-assisted virtualization"
    };

    // Shared by qemu-system-aarch64 and qemu-system-arm, which list the same names.
    private static readonly IReadOnlyDictionary<string, string> Arm = new Dictionary<string, string>
    {
        ["a64fx"] = "Fujitsu A64FX, Armv8.2-A with SVE (the Fugaku supercomputer)",
        ["arm1026"] = "ARM1026EJ-S, Armv5TEJ",
        ["arm1136"] = "ARM1136, Armv6 (revision r1)",
        ["arm1136-r2"] = "ARM1136, Armv6 (revision r2)",
        ["arm1176"] = "ARM1176JZF-S, Armv6KZ (the original Raspberry Pi)",
        ["arm11mpcore"] = "ARM11 MPCore, multiprocessor Armv6K",
        ["arm926"] = "ARM926EJ-S, Armv5TEJ",
        ["arm946"] = "ARM946E-S, Armv5TE with a memory protection unit",
        ["cortex-a15"] = "Arm Cortex-A15, Armv7-A (32-bit)",
        ["cortex-a35"] = "Arm Cortex-A35, Armv8-A (64-bit, low power)",
        ["cortex-a53"] = "Arm Cortex-A53, Armv8-A (64-bit)",
        ["cortex-a55"] = "Arm Cortex-A55, Armv8.2-A",
        ["cortex-a57"] = "Arm Cortex-A57, Armv8-A (64-bit)",
        ["cortex-a7"] = "Arm Cortex-A7, Armv7-A (32-bit, low power)",
        ["cortex-a710"] = "Arm Cortex-A710, Armv9.0-A",
        ["cortex-a72"] = "Arm Cortex-A72, Armv8-A (64-bit)",
        ["cortex-a76"] = "Arm Cortex-A76, Armv8.2-A",
        ["cortex-a78ae"] = "Arm Cortex-A78AE, Armv8.2-A (automotive)",
        ["cortex-a8"] = "Arm Cortex-A8, Armv7-A (32-bit)",
        ["cortex-a9"] = "Arm Cortex-A9, Armv7-A (32-bit)",
        ["cortex-m0"] = "Arm Cortex-M0 microcontroller, Armv6-M",
        ["cortex-m3"] = "Arm Cortex-M3 microcontroller, Armv7-M",
        ["cortex-m33"] = "Arm Cortex-M33 microcontroller, Armv8-M with TrustZone",
        ["cortex-m4"] = "Arm Cortex-M4 microcontroller, Armv7E-M",
        ["cortex-m55"] = "Arm Cortex-M55 microcontroller, Armv8.1-M",
        ["cortex-m7"] = "Arm Cortex-M7 microcontroller, Armv7E-M",
        ["cortex-r5"] = "Arm Cortex-R5 real-time core, Armv7-R",
        ["cortex-r52"] = "Arm Cortex-R52 real-time core, Armv8-R",
        ["cortex-r5f"] = "Arm Cortex-R5F real-time core with FPU, Armv7-R",
        ["max"] = "Everything QEMU can emulate: the most capable Arm CPU, not a real chip",
        ["neoverse-n1"] = "Arm Neoverse N1 server core, Armv8.2-A",
        ["neoverse-n2"] = "Arm Neoverse N2 server core, Armv9.0-A",
        ["neoverse-v1"] = "Arm Neoverse V1 server core, Armv8.4-A with SVE",
        ["sa1100"] = "Intel StrongARM SA-1100, Armv4",
        ["sa1110"] = "Intel StrongARM SA-1110, Armv4",
        ["ti925t"] = "Texas Instruments TI925T (ARM925T), Armv4T"
    };

    private static readonly IReadOnlyDictionary<string, string> RiscV = new Dictionary<string, string>
    {
        ["lowrisc-ibex"] = "lowRISC Ibex, small 32-bit embedded core",
        ["max"] = "Everything QEMU can emulate: all supported extensions on, not a real chip",
        ["max32"] = "Like max, with 32-bit registers",
        ["mips-p8700"] = "MIPS P8700, a 64-bit RISC-V core",
        ["rv32"] = "Generic 32-bit RISC-V; choose extensions with -cpu rv32,<ext>=on",
        ["rv32e"] = "Generic 32-bit RISC-V with 16 registers (RV32E)",
        ["rv32i"] = "Generic 32-bit RISC-V, base integer instructions only (RV32I)",
        ["rv64"] = "Generic 64-bit RISC-V; choose extensions with -cpu rv64,<ext>=on",
        ["rv64e"] = "Generic 64-bit RISC-V with 16 registers (RV64E)",
        ["rv64i"] = "Generic 64-bit RISC-V, base integer instructions only (RV64I)",
        ["rva22s64"] = "RISC-V RVA22 profile, 64-bit, supervisor mode",
        ["rva22u64"] = "RISC-V RVA22 profile, 64-bit, user mode",
        ["rva23s64"] = "RISC-V RVA23 profile, 64-bit, supervisor mode",
        ["rva23u64"] = "RISC-V RVA23 profile, 64-bit, user mode",
        ["shakti-c"] = "SHAKTI C-class core (IIT Madras), 64-bit",
        ["sifive-e31"] = "SiFive E31, 32-bit embedded core",
        ["sifive-e34"] = "SiFive E34, 32-bit embedded core with FPU",
        ["sifive-e51"] = "SiFive E51, 64-bit embedded core",
        ["sifive-u34"] = "SiFive U34, 32-bit application core",
        ["sifive-u54"] = "SiFive U54, 64-bit application core (HiFive Unleashed)",
        ["thead-c906"] = "T-Head XuanTie C906, 64-bit",
        ["thead-c908"] = "T-Head XuanTie C908, 64-bit",
        ["thead-c908v"] = "T-Head XuanTie C908, vector-extension variant",
        ["tt-ascalon"] = "Tenstorrent Ascalon, high-performance 64-bit",
        ["veyron-v1"] = "Ventana Veyron V1, 64-bit",
        ["x-rv128"] = "Experimental 128-bit RISC-V (x- marks an experimental CPU)",
        ["xiangshan-kunminghu"] = "XiangShan Kunminghu, open-source 64-bit core",
        ["xiangshan-nanhu"] = "XiangShan Nanhu, open-source 64-bit core"
    };

    // Shared by qemu-system-mips and qemu-system-mips64. Release levels and years come from
    // https://www.qemu.org/docs/master/system/qemu-cpu-models.html ("Supported CPU models for
    // MIPS32 / MIPS64 hosts"); the feature notes (DSP, FPU, MT) are the cores' own names.
    private static readonly IReadOnlyDictionary<string, string> Mips = new Dictionary<string, string>
    {
        ["4Kc"] = "MIPS32, 4K family (1999)",
        ["4Km"] = "MIPS32, 4K family (1999)",
        ["4KEc"] = "MIPS32 Release 2, 4K family (1999)",
        ["4KEcR1"] = "MIPS32 Release 1, 4KEc variant (1999)",
        ["4KEm"] = "MIPS32 Release 2, 4K family (1999)",
        ["4KEmR1"] = "MIPS32 Release 1, 4KEm variant (1999)",
        ["24Kc"] = "MIPS32 Release 2, 24K family (2003)",
        ["24KEc"] = "MIPS32 Release 2 with DSP, 24K family (2003)",
        ["24Kf"] = "MIPS32 Release 2 with FPU, 24K family (2003)",
        ["34Kf"] = "MIPS32 Release 2 with multithreading and FPU, 34K family (2006)",
        ["74Kf"] = "MIPS32 Release 2 with DSP and FPU, 74K family (2007)",
        ["M14K"] = "MIPS32 microMIPS microcontroller core, M14K family (2009)",
        ["M14Kc"] = "MIPS32 microMIPS core with an MMU, M14K family (2009)",
        ["P5600"] = "MIPS32 Release 5, P5600 (2014)",
        ["mips32r6-generic"] = "Generic MIPS32 Release 6 (2015)",
        ["I7200"] = "MIPS I7200, nanoMIPS",
        ["XBurstR1"] = "Ingenic XBurst R1 (JZ4740)",
        ["XBurstR2"] = "Ingenic XBurst R2",
        ["R4000"] = "MIPS III, R4000 (1991)",
        ["VR5432"] = "MIPS64, NEC VR5432 (1998)",
        ["5Kc"] = "MIPS64, 5K family (1999)",
        ["5Kf"] = "MIPS64 with FPU, 5K family (1999)",
        ["20Kc"] = "MIPS64, 20K family (2000)",
        ["5KEc"] = "MIPS64 Release 2, 5K family (2002)",
        ["5KEf"] = "MIPS64 Release 2 with FPU, 5K family (2002)",
        ["MIPS64R2-generic"] = "Generic MIPS64 Release 2 (2002)",
        ["mips64dspr2"] = "MIPS64 Release 2 with DSP Release 2 (2006)",
        ["I6400"] = "MIPS64 Release 6, I6400 (2014)",
        ["I6500"] = "MIPS64 Release 6, I6500, multi-cluster",
        ["Loongson-2E"] = "MIPS64, Loongson 2E (2006)",
        ["Loongson-2F"] = "MIPS64, Loongson 2F (2008)",
        ["Loongson-3A1000"] = "MIPS64, Loongson 3A1000 (2010)",
        ["Loongson-3A4000"] = "MIPS64, Loongson 3A4000 (2018)",
        ["Octeon68XX"] = "Cavium Octeon CN68XX"
    };

    // PowerPC lists some 400 names and gives only a PVR for most, so these are rules over the
    // family named in the identifier, not a table. First match wins; order matters.
    private static readonly (Regex Pattern, string Label)[] PpcFamilies =
    [
        (R("^power(?<n>[0-9]+)(?<plus>p?)(_|$)"), "IBM POWER"),
        (R("^ppe42"), "IBM PPE42 embedded core (used inside POWER processors)"),
        (R("^970"), "PowerPC 970 (G5)"),
        (R("^74[0-9][0-9]"), "PowerPC 74xx (G4)"),
        (R("^7[45][05]"), "PowerPC 7xx (G3)"),
        (R("^(603|mpc603)"), "PowerPC 603"),
        (R("^604"), "PowerPC 604"),
        (R("^440-xilinx"), "PowerPC 440 as used in the Xilinx Virtex-5 FX"),
        (R("^(405|440|460)"), "IBM PowerPC 4xx embedded core"),
        (R("^npe4"), "AMCC PowerPC NPe4xx network processor"),
        (R("^stb"), "IBM PowerPC STB set-top-box SoC"),
        (R("^x2vp"), "Xilinx Virtex-II Pro (PowerPC 405)"),
        (R("^e300"), "Freescale e300 core (MPC83xx)"),
        (R("^g2"), "Freescale PowerPC G2 core (MPC82xx)"),
        (R("^e500mc"), "Freescale e500mc, multicore e500"),
        (R("^e5500"), "Freescale e5500, 64-bit e500"),
        (R("^e6500"), "Freescale e6500, 64-bit multithreaded"),
        (R("^e500"), "Freescale e500 core (PowerQUICC III, MPC85xx)"),
        (R("^e600"), "Freescale e600 core (G4-class, MPC86xx)"),
        (R("^mpc5200"), "Freescale MPC5200 SoC (e300 core)"),
        (R("^mpc83"), "Freescale MPC83xx PowerQUICC II Pro (e300 core)"),
        (R("^mpc85"), "Freescale MPC85xx PowerQUICC III (e500 core)"),
        (R("^mpc86"), "Freescale MPC86xx (e600 core)")
    ];

    // QEMU writes revision 3.0 as "30" and 2.1 as "21"; the PVR's low byte (…0030, …0021) agrees.
    private static string FormatRevision(string raw)
        => raw.Length == 2 && char.IsDigit(raw[0]) && char.IsDigit(raw[1]) ? $"{raw[0]}.{raw[1]}" : raw;

    private static Regex R(string pattern) => new(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string Ppc(string name, string qemuText)
    {
        foreach (var (pattern, label) in PpcFamilies)
        {
            var match = pattern.Match(name);
            if (!match.Success)
                continue;

            var parts = new List<string>
            {
                match.Groups["n"].Success
                    ? $"{label}{match.Groups["n"].Value}{(match.Groups["plus"].Value.Length > 0 ? "+" : "")}"
                    : label
            };

            var revision = Revision.Match(name);
            if (revision.Success)
                parts.Add($"version {FormatRevision(revision.Groups["rev"].Value)}");

            // The PVR is the one fact QEMU does give, so it stays.
            if (qemuText.Length > 0)
                parts.Add(qemuText);

            return string.Join("; ", parts);
        }

        return qemuText;
    }
}
