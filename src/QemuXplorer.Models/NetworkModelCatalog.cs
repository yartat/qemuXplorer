using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models;

/// <summary>One network card QEMU can emulate: the device name it expects and what the card is for.</summary>
public sealed record NetworkModelInfo(NetworkModel Model, string QemuName, string Label, string Description)
{
    public override string ToString() => Label;
}

/// <summary>
/// Everything the app knows about the emulated network cards. The device names are what goes after
/// <c>-device</c>; the descriptions say which card to pick, since the right one depends on the guest's drivers.
/// </summary>
public static class NetworkModelCatalog
{
    public static IReadOnlyList<NetworkModelInfo> All { get; } =
    [
        new(NetworkModel.VirtIoNetPci, "virtio-net-pci", "VirtIO (virtio-net-pci)",
            "Paravirtualized card: the fastest and lightest, and the usual choice. The guest needs a VirtIO driver; "
            + "Linux has one built in, Windows needs the virtio-win drivers installed."),
        new(NetworkModel.E1000, "e1000", "Intel 82540EM (e1000)",
            "Emulates an Intel Gigabit card that nearly every operating system already has a driver for. "
            + "Slower than VirtIO, but works without installing anything."),
        new(NetworkModel.E1000E, "e1000e", "Intel 82574L (e1000e)",
            "Emulates a newer Intel Gigabit PCI Express card. Use it when a recent Windows or Linux guest does not "
            + "take the older e1000."),
        new(NetworkModel.Rtl8139, "rtl8139", "Realtek RTL8139",
            "Emulates a 100 Mbit Realtek card from the late 1990s. For older guests that have a driver for it and "
            + "none for e1000."),
        new(NetworkModel.Ne2kPci, "ne2k_pci", "NE2000 PCI (ne2k_pci)",
            "Emulates a 10 Mbit NE2000 card, the oldest and slowest. Only for very old guests whose driver "
            + "list stops there."),
        new(NetworkModel.Vmxnet3, "vmxnet3", "VMware vmxnet3",
            "Emulates VMware's paravirtualized card. Pick it for a guest that came from VMware or already carries "
            + "VMware's driver; elsewhere VirtIO is simpler."),
        new(NetworkModel.Pcnet, "pcnet", "AMD PCnet-PCI II (pcnet)",
            "Emulates a 10/100 Mbit AMD card that old operating systems and some non-PC machines support. "
            + "Slow; for guests that need it.")
    ];

    private static readonly Dictionary<NetworkModel, NetworkModelInfo> ByModel = All.ToDictionary(m => m.Model);

    public static NetworkModelInfo Get(NetworkModel model) => ByModel[model];

    /// <summary>The name <c>-device</c> expects.</summary>
    public static string QemuName(NetworkModel model) => ByModel[model].QemuName;
}
