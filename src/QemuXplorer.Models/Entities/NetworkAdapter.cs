using QemuXplorer.Models.Enums;

namespace QemuXplorer.Models.Entities;

public class NetworkAdapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VmId { get; set; }
    public NetworkBackendType BackendType { get; set; } = NetworkBackendType.User;
    public NetworkModel Model { get; set; } = NetworkModel.VirtIoNetPci;
    public string MacAddress { get; set; } = string.Empty;

    /// <summary>User backend: JSON list of <see cref="PortForward"/> (see <see cref="PortForwardRules"/>).</summary>
    public string HostForwardingRules { get; set; } = "[]";

    public string BridgeInterface { get; set; } = string.Empty;
    public string TapInterface { get; set; } = string.Empty;

    // User backend. Blank means "QEMU's default".
    public string UserSubnet { get; set; } = string.Empty;
    public string UserDhcpStart { get; set; } = string.Empty;
    public string UserDns { get; set; } = string.Empty;
    public string UserHostname { get; set; } = string.Empty;
    public bool UserRestrict { get; set; }

    // Tap backend. Blank scripts mean "run none" (script=no), the safe default.
    public string TapScript { get; set; } = string.Empty;
    public string TapDownScript { get; set; } = string.Empty;

    // Socket backend. For Listen only the port matters; Multicast uses the address as the group.
    public NetworkSocketMode SocketMode { get; set; } = NetworkSocketMode.Listen;
    public string SocketHost { get; set; } = string.Empty;
    public int SocketPort { get; set; }
    public string SocketLocalHost { get; set; } = string.Empty;
    public int SocketLocalPort { get; set; }

    public VirtualMachine? VirtualMachine { get; set; }

    /// <summary>One line for lists. Computed, so it is not stored.</summary>
    public string Summary => BackendType switch
    {
        NetworkBackendType.User => PortForwardRules.Parse(HostForwardingRules) is { Count: > 0 } rules
            ? $"NAT, {rules.Count} port forward{(rules.Count == 1 ? string.Empty : "s")}"
            : "NAT",
        NetworkBackendType.Tap => $"tap {(TapInterface.Length > 0 ? TapInterface : "(unnamed)")}",
        NetworkBackendType.Bridge => $"bridge {(BridgeInterface.Length > 0 ? BridgeInterface : "(unnamed)")}",
        NetworkBackendType.Socket => SocketMode switch
        {
            NetworkSocketMode.Listen => $"listen on port {SocketPort}",
            NetworkSocketMode.Connect => $"connect to {SocketHost}:{SocketPort}",
            NetworkSocketMode.Multicast => $"multicast {SocketHost}:{SocketPort}",
            _ => $"udp {SocketHost}:{SocketPort}"
        },
        _ => string.Empty
    };
}
