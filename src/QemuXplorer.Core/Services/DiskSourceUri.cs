using QemuXplorer.Models.Enums;

namespace QemuXplorer.Core.Services;

/// <summary>
/// Builds and recognises the URIs QEMU accepts in <c>-drive file=</c> for network-backed disks.
/// The structured pieces the wizard collects (host, port, export...) are folded into
/// <c>DiskDevice.FilePath</c>, which is exactly what QEMU consumes, so no extra columns or a
/// parallel representation are needed.
/// </summary>
public static class DiskSourceUri
{
    public const int NbdDefaultPort = 10809;
    public const int SshDefaultPort = 22;
    public const int IscsiDefaultPort = 3260;

    /// <summary>nbd://host:port/export — the export name is optional.</summary>
    public static string Nbd(string host, int port, string? export = null)
    {
        var uri = $"nbd://{Host(host)}:{port}";
        return string.IsNullOrWhiteSpace(export) ? uri : $"{uri}/{Uri.EscapeDataString(export.Trim())}";
    }

    /// <summary>ssh://[user@]host:port/absolute/path — QEMU takes the path as absolute.</summary>
    public static string Ssh(string? user, string host, int port, string remotePath)
    {
        var segments = remotePath.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return $"ssh://{User(user)}{Host(host)}:{port}/{string.Join('/', segments)}";
    }

    /// <summary>iscsi://[user@]host:port/target-iqn/lun.</summary>
    public static string Iscsi(string? user, string host, int port, string targetIqn, int lun)
        => $"iscsi://{User(user)}{Host(host)}:{port}/{Uri.EscapeDataString(targetIqn.Trim())}/{lun}";

    public static DiskSourceKind Classify(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return DiskSourceKind.LocalFile;

        if (filePath.StartsWith("nbd://", StringComparison.OrdinalIgnoreCase)) return DiskSourceKind.Nbd;
        if (filePath.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)) return DiskSourceKind.Ssh;
        if (filePath.StartsWith("iscsi://", StringComparison.OrdinalIgnoreCase)) return DiskSourceKind.Iscsi;
        return DiskSourceKind.LocalFile;
    }

    /// <summary>The block driver QEMU needs for the source, or null for a local file.</summary>
    public static string? DriverFor(DiskSourceKind kind) => kind switch
    {
        DiskSourceKind.Nbd => "nbd",
        DiskSourceKind.Ssh => "ssh",
        DiskSourceKind.Iscsi => "iscsi",
        _ => null
    };

    private static string Host(string host)
    {
        host = host.Trim();
        // A bare IPv6 address must be bracketed or its colons read as a port separator.
        return host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
    }

    private static string User(string? user)
        => string.IsNullOrWhiteSpace(user) ? string.Empty : $"{Uri.EscapeDataString(user.Trim())}@";
}
