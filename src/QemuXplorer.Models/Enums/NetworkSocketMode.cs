namespace QemuXplorer.Models.Enums;

/// <summary>How a <see cref="NetworkBackendType.Socket"/> backend joins the other machines.</summary>
public enum NetworkSocketMode
{
    /// <summary>Wait for another machine to connect over TCP (<c>listen=</c>).</summary>
    Listen,
    /// <summary>Connect to a machine that is listening (<c>connect=</c>).</summary>
    Connect,
    /// <summary>Share one multicast group with any number of machines (<c>mcast=</c>).</summary>
    Multicast,
    /// <summary>Point-to-point UDP (<c>udp=</c> with <c>localaddr=</c>).</summary>
    Udp
}
