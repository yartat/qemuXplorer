namespace QemuXplorer.Models.Enums;

/// <summary>
/// Ties a <see cref="MachineType"/> to the QEMU machine name and to every architecture whose
/// emulator accepts it.
/// </summary>
/// <remarks>
/// The architecture relation is many-to-many — <c>virt</c> is offered by aarch64, arm, riscv64
/// and riscv32, and <c>none</c> by all of them — so it cannot be expressed by the single string
/// of <c>Display.GroupName</c>.
///
/// <see cref="QemuName"/> is separate from the enum member name because QEMU names contain
/// characters a C# identifier cannot: <c>ast2500-evb</c>, <c>40p</c>, <c>b-l475e-iot01a</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class MachineAttribute : Attribute
{
    public MachineAttribute(string qemuName, params GuestArchitecture[] architectures)
    {
        QemuName = qemuName;
        Architectures = architectures;
    }

    /// <summary>The name passed to <c>-machine type=</c>.</summary>
    public string QemuName { get; }

    /// <summary>Architectures whose emulator offers this machine. Never empty.</summary>
    public GuestArchitecture[] Architectures { get; }

    /// <summary>QEMU marks the machine deprecated; it still runs, for now.</summary>
    public bool IsDeprecated { get; init; }
}
