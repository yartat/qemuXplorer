using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.Models.Entities;

public class NetworkAdapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VmId { get; set; }
    public NetworkBackendType BackendType { get; set; } = NetworkBackendType.User;
    public NetworkModel Model { get; set; } = NetworkModel.VirtIoNetPci;
    public string MacAddress { get; set; } = string.Empty;
    public string HostForwardingRules { get; set; } = "[]"; // JSON
    public string BridgeInterface { get; set; } = string.Empty;
    public string TapInterface { get; set; } = string.Empty;

    public VirtualMachine? VirtualMachine { get; set; }
}
