using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models;

public enum ImageOptionKind
{
    Text,
    Number,
    Bool,
    Choice
}

/// <summary>
/// One <c>qemu-img create -o key=value</c> option. <see cref="Default"/> is QEMU's own default:
/// a value equal to it (or empty) is left off the command line.
/// </summary>
public sealed record ImageOption(
    string Key,
    string Label,
    string Description,
    ImageOptionKind Kind,
    string Default = "",
    IReadOnlyList<string>? Choices = null);

public sealed record DiskFormatInfo(
    DiskFormat Format,
    string QemuName,
    string Label,
    string Description,
    bool CanCreate,
    bool IsReadOnly,
    IReadOnlyList<ImageOption> CreateOptions)
{
    public bool NeedsKeySecret => Format == DiskFormat.Luks;

    // What qemu-img can do to an existing image of this format. Measured with qemu-img 11.1:
    // resize works on raw, qcow2 and qed (qed cannot shrink), "rebase" on qcow2 and qed,
    // "amend" on qcow2, and "check" on qcow2, qed, vdi, vmdk and vhdx. Everything can be converted.
    public bool CanResize => Format is DiskFormat.QCow2 or DiskFormat.Raw or DiskFormat.Qed;
    public bool CanShrink => Format is DiskFormat.QCow2 or DiskFormat.Raw;
    public bool CanRebase => Format is DiskFormat.QCow2 or DiskFormat.Qed;
    public bool CanAmend => Format == DiskFormat.QCow2;
    public bool CanCheck => Format is DiskFormat.QCow2 or DiskFormat.Qed or DiskFormat.Vdi or DiskFormat.Vmdk or DiskFormat.Vhdx;

    public override string ToString() => Label;
}

/// <summary>
/// Everything the app knows about a disk image format. Option lists were taken from
/// <c>qemu-img create -f &lt;fmt&gt; -o help</c> on QEMU 11.1 and trimmed to the ones worth a
/// field; encryption options are omitted because LUKS is its own format here.
/// </summary>
public static class DiskFormatCatalog
{
    private static ImageOption Backing() => new(
        "backing_file", "Backing file", "Base image this one stores differences from",
        ImageOptionKind.Text);

    private static ImageOption BackingFormat() => new(
        "backing_fmt", "Backing format", "Format of the backing file; set it, or QEMU must probe",
        ImageOptionKind.Text);

    /// <summary>
    /// qcow2 preallocation modes the platform's QEMU can actually perform. On Windows the file driver
    /// cannot resize with <c>falloc</c> or <c>full</c> (<c>qemu-img</c> 11.1 fails with "Unsupported
    /// preallocation mode"), although <c>-o help</c> lists all four, so only the first two are offered.
    /// </summary>
    public static IReadOnlyList<string> Qcow2PreallocationModes(bool windows)
        => windows ? ["off", "metadata"] : ["off", "metadata", "falloc", "full"];

    /// <summary>The qcow2 settings <c>qemu-img amend</c> can change on an existing image.</summary>
    public static IReadOnlyList<ImageOption> Qcow2AmendOptions { get; } =
    [
        new("compat", "Compatibility", "v3 (1.1) is current; 0.10 is for ancient QEMU",
            ImageOptionKind.Choice, Choices: ["1.1", "0.10"]),
        new("lazy_refcounts", "Lazy refcounts", "Postpone refcount updates: faster, slower crash recovery",
            ImageOptionKind.Choice, Choices: ["on", "off"]),
        new("refcount_bits", "Refcount bits", "Width of a reference count entry",
            ImageOptionKind.Number)
    ];

    public static IReadOnlyList<DiskFormatInfo> All { get; } =
    [
        new(DiskFormat.QCow2, "qcow2", "qcow2",
            "QEMU copy-on-write v2/v3: snapshots, compression, thin provisioning",
            CanCreate: true, IsReadOnly: false,
            [
                new("preallocation", "Preallocation",
                    OperatingSystem.IsWindows()
                        ? "How much space to reserve up front. QEMU on Windows cannot do falloc or full"
                        : "How much space to reserve up front",
                    ImageOptionKind.Choice, Choices: Qcow2PreallocationModes(OperatingSystem.IsWindows())),
                new("cluster_size", "Cluster size", "Bytes per cluster, a power of two (e.g. 65536)",
                    ImageOptionKind.Text),
                new("compat", "Compatibility", "v3 (1.1) is current; 0.10 is for ancient QEMU",
                    ImageOptionKind.Choice, Choices: ["1.1", "0.10"]),
                new("compression_type", "Compression", "Method for compressed clusters",
                    ImageOptionKind.Choice, Choices: ["zlib", "zstd"]),
                new("lazy_refcounts", "Lazy refcounts", "Postpone refcount updates: faster, slower crash recovery",
                    ImageOptionKind.Bool, "off"),
                new("extended_l2", "Extended L2", "Sub-cluster allocation tables",
                    ImageOptionKind.Bool, "off"),
                new("refcount_bits", "Refcount bits", "Width of a reference count entry",
                    ImageOptionKind.Number),
                Backing(),
                BackingFormat()
            ]),

        new(DiskFormat.Qcow, "qcow", "qcow",
            "Legacy QEMU copy-on-write v1; prefer qcow2",
            CanCreate: true, IsReadOnly: false,
            [Backing(), BackingFormat()]),

        new(DiskFormat.Qed, "qed", "qed",
            "QEMU Enhanced Disk: the short-lived qcow2 alternative",
            CanCreate: true, IsReadOnly: false,
            [
                new("cluster_size", "Cluster size", "Bytes per cluster", ImageOptionKind.Text),
                new("table_size", "Table size", "L1/L2 table size, in clusters", ImageOptionKind.Text),
                Backing(),
                BackingFormat()
            ]),

        new(DiskFormat.Raw, "raw", "raw",
            "Plain byte-for-byte image; fastest, no features",
            CanCreate: true, IsReadOnly: false, []),

        new(DiskFormat.Vdi, "vdi", "vdi",
            "VirtualBox disk image",
            CanCreate: true, IsReadOnly: false,
            [new("static", "Pre-allocated", "Reserve the whole disk up front",
                ImageOptionKind.Bool, "off")]),

        new(DiskFormat.Vmdk, "vmdk", "vmdk",
            "VMware virtual disk",
            CanCreate: true, IsReadOnly: false,
            [
                new("subformat", "Sub-format", "Extent layout",
                    ImageOptionKind.Choice, Choices:
                        ["monolithicSparse", "monolithicFlat", "twoGbMaxExtentSparse",
                         "twoGbMaxExtentFlat", "streamOptimized"]),
                new("adapter_type", "Adapter", "Virtual adapter the descriptor claims",
                    ImageOptionKind.Choice, Choices: ["ide", "lsilogic", "buslogic", "legacyESX"]),
                new("hwversion", "Hardware version", "VMDK hardware version", ImageOptionKind.Text),
                new("compat6", "VMDK 6", "Write a version 6 image", ImageOptionKind.Bool, "off"),
                new("zeroed_grain", "Zeroed grain", "Efficient zero writes", ImageOptionKind.Bool, "off"),
                Backing()
            ]),

        new(DiskFormat.Vhd, "vpc", "vpc (VHD)",
            "Microsoft Virtual PC / Hyper-V VHD; QEMU calls the driver vpc",
            CanCreate: true, IsReadOnly: false,
            [
                new("subformat", "Sub-format", "Dynamic grows on demand; fixed reserves it all",
                    ImageOptionKind.Choice, Choices: ["dynamic", "fixed"]),
                new("force_size", "Force size", "Use the exact size instead of rounding to CHS geometry",
                    ImageOptionKind.Bool, "off")
            ]),

        new(DiskFormat.Vhdx, "vhdx", "vhdx",
            "Microsoft Hyper-V VHDX",
            CanCreate: true, IsReadOnly: false,
            [
                new("subformat", "Sub-format", "Dynamic grows on demand; fixed reserves it all",
                    ImageOptionKind.Choice, Choices: ["dynamic", "fixed"]),
                new("block_size", "Block size", "1 MB to 256 MB; 0 picks automatically",
                    ImageOptionKind.Text),
                new("log_size", "Log size", "At least 1 MB", ImageOptionKind.Text),
                new("block_state_zero", "Zero blocks", "Non-standard but default; leave on with subformat=dynamic",
                    ImageOptionKind.Bool, "on")
            ]),

        new(DiskFormat.Luks, "luks", "luks",
            "LUKS-encrypted image; needs a passphrase file",
            CanCreate: true, IsReadOnly: false,
            [
                new("cipher-alg", "Cipher", "e.g. aes-256", ImageOptionKind.Text),
                new("cipher-mode", "Cipher mode", "e.g. xts", ImageOptionKind.Text),
                new("hash-alg", "Hash", "e.g. sha256", ImageOptionKind.Text),
                new("iter-time", "KDF time (ms)", "Time spent in PBKDF", ImageOptionKind.Number)
            ]),

        new(DiskFormat.Parallels, "parallels", "parallels",
            "Parallels Desktop image; attached read-only here",
            CanCreate: false, IsReadOnly: true, []),

        new(DiskFormat.Bochs, "bochs", "bochs",
            "Bochs growing disk image; QEMU can only read it",
            CanCreate: false, IsReadOnly: true, []),

        new(DiskFormat.Cloop, "cloop", "cloop",
            "Compressed loop image (Knoppix); QEMU can only read it",
            CanCreate: false, IsReadOnly: true, []),

        new(DiskFormat.Dmg, "dmg", "dmg",
            "Apple disk image; QEMU can only read it",
            CanCreate: false, IsReadOnly: true, [])
    ];

    private static readonly Dictionary<DiskFormat, DiskFormatInfo> ByFormat =
        All.ToDictionary(f => f.Format);

    private static readonly Dictionary<string, DiskFormatInfo> ByQemuName =
        All.ToDictionary(f => f.QemuName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<DiskFormatInfo> Creatable { get; } =
        All.Where(f => f.CanCreate).ToList();

    public static DiskFormatInfo Get(DiskFormat format) => ByFormat[format];

    /// <summary>The name <c>-drive format=</c> and <c>qemu-img -f</c> expect.</summary>
    public static string QemuName(DiskFormat format) => ByFormat[format].QemuName;

    /// <summary>Maps a driver name back, accepting "vhd" as an alias for vpc.</summary>
    public static DiskFormat? FromQemuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (name.Equals("vhd", StringComparison.OrdinalIgnoreCase))
            return DiskFormat.Vhd;
        return ByQemuName.TryGetValue(name, out var info) ? info.Format : null;
    }

    public static bool IsReadOnly(DiskFormat format) => ByFormat[format].IsReadOnly;
}
