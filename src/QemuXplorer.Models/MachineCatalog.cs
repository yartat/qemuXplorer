using System.ComponentModel.DataAnnotations;
using System.Reflection;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models;

/// <summary>One machine type, flattened out of the attributes on <see cref="MachineType"/>.</summary>
public sealed record MachineTypeInfo(
    MachineType Value,
    string QemuName,
    string Description,
    IReadOnlyList<GuestArchitecture> Architectures,
    bool IsDeprecated)
{
    public bool SupportsArchitecture(GuestArchitecture architecture)
        => Architectures.Contains(architecture);

    /// <summary>"q35 — Standard PC (Q35 + ICH9, 2009)", for a picker.</summary>
    public string DisplayLabel => string.IsNullOrEmpty(Description)
        ? QemuName
        : $"{QemuName} — {Description}";

    public override string ToString() => QemuName;
}

/// <summary>
/// Reads the <see cref="MachineAttribute"/> / <see cref="DisplayAttribute"/> pairs on
/// <see cref="MachineType"/> once, then answers lookups from memory.
/// </summary>
public static class MachineCatalog
{
    private static readonly IReadOnlyList<MachineTypeInfo> AllMachines = Build();

    private static readonly Dictionary<string, MachineTypeInfo> ByName =
        AllMachines.ToDictionary(m => m.QemuName, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<GuestArchitecture, IReadOnlyList<MachineTypeInfo>> ByArchitecture =
        Enum.GetValues<GuestArchitecture>().ToDictionary(
            arch => arch,
            arch => (IReadOnlyList<MachineTypeInfo>)AllMachines
                .Where(m => m.SupportsArchitecture(arch))
                .ToList());

    public static IReadOnlyList<MachineTypeInfo> All => AllMachines;

    /// <summary>
    /// Machines the given architecture's emulator offers, in declaration order so the common
    /// choice for that architecture comes first.
    /// </summary>
    public static IReadOnlyList<MachineTypeInfo> ForArchitecture(GuestArchitecture architecture)
        => ByArchitecture.TryGetValue(architecture, out var list) ? list : [];

    /// <summary>The default machine for an architecture, or null if none is known.</summary>
    public static MachineTypeInfo? DefaultFor(GuestArchitecture architecture)
        => ForArchitecture(architecture).FirstOrDefault(m => !m.IsDeprecated)
           ?? ForArchitecture(architecture).FirstOrDefault();

    /// <summary>
    /// Looks up a QEMU machine name. Returns null for anything this build does not know,
    /// including the versioned variants (pc-q35-9.2) the enum deliberately omits — callers
    /// must treat null as "unrecognised but possibly valid", not as an error.
    /// </summary>
    public static MachineTypeInfo? Find(string? qemuName)
        => string.IsNullOrWhiteSpace(qemuName) ? null
           : ByName.TryGetValue(qemuName, out var info) ? info : null;

    public static MachineTypeInfo Get(MachineType value)
        => AllMachines.First(m => m.Value == value);

    /// <summary>True when the architecture's emulator is known to accept the machine name.</summary>
    public static bool IsValidFor(string? qemuName, GuestArchitecture architecture)
        => Find(qemuName)?.SupportsArchitecture(architecture) ?? false;

    private static IReadOnlyList<MachineTypeInfo> Build()
    {
        var results = new List<MachineTypeInfo>();

        foreach (var field in typeof(MachineType).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var machine = field.GetCustomAttribute<MachineAttribute>();
            if (machine is null)
                continue;

            var display = field.GetCustomAttribute<DisplayAttribute>();

            results.Add(new MachineTypeInfo(
                (MachineType)field.GetValue(null)!,
                machine.QemuName,
                display?.Description ?? string.Empty,
                machine.Architectures,
                machine.IsDeprecated));
        }

        return results;
    }
}
