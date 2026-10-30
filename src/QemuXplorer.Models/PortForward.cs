using System.Text.Json;

namespace QemuXplorer.Models;

/// <summary>One <c>hostfwd=</c> rule of a user-mode network: a host port that reaches a guest port.</summary>
public sealed record PortForward(string Protocol, string HostAddress, int HostPort, string GuestAddress, int GuestPort)
{
    /// <summary>The value QEMU expects after <c>hostfwd=</c>: <c>tcp:127.0.0.1:2222-:22</c>.</summary>
    public string ToHostFwd() => $"{Protocol}:{HostAddress}:{HostPort}-{GuestAddress}:{GuestPort}";

    public override string ToString() =>
        $"{Protocol.ToUpperInvariant()} {(HostAddress.Length > 0 ? HostAddress : "*")}:{HostPort} -> {(GuestAddress.Length > 0 ? GuestAddress : "guest")}:{GuestPort}";
}

/// <summary>Reads and writes the JSON list kept in <c>NetworkAdapter.HostForwardingRules</c>.</summary>
public static class PortForwardRules
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>An empty or unreadable value is an empty list, so one bad row cannot stop a machine opening.</summary>
    public static IReadOnlyList<PortForward> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<PortForward>>(json, Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string ToJson(IEnumerable<PortForward> rules) => JsonSerializer.Serialize(rules.ToList(), Options);
}
