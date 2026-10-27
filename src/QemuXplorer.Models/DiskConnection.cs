using System.Text.Json;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models;

/// <summary>
/// Connection parameters for a disk that is not just a local file. Persisted as JSON in
/// <c>DiskDevice.ConnectionJson</c>; only the fields relevant to <see cref="Kind"/> are set.
/// Passwords are deliberately absent: secrets reach QEMU through files, never through the
/// database or the command line.
/// </summary>
public sealed record DiskConnection
{
    public DiskSourceKind Kind { get; init; } = DiskSourceKind.LocalFile;
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string User { get; init; } = string.Empty;

    /// <summary>NBD export name.</summary>
    public string Export { get; init; } = string.Empty;

    /// <summary>Absolute path on the SSH server.</summary>
    public string RemotePath { get; init; } = string.Empty;

    /// <summary>iSCSI target IQN.</summary>
    public string Target { get; init; } = string.Empty;

    public int Lun { get; init; }

    /// <summary>File holding the LUKS passphrase; only its path is stored.</summary>
    public string KeySecretFile { get; init; } = string.Empty;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Null for an empty or unreadable value, so a bad row degrades to "no connection".</summary>
    public static DiskConnection? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<DiskConnection>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
