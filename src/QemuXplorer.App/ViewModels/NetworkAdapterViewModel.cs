using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.App.ViewModels;

public sealed record BackendChoice(NetworkBackendType Type, string Title, string Description);

/// <summary>One editable <c>hostfwd</c> rule.</summary>
public partial class PortForwardViewModel : ObservableObject
{
    [ObservableProperty] private string _protocol = "tcp";
    [ObservableProperty] private string _hostAddress = string.Empty;
    [ObservableProperty] private int _hostPort;
    [ObservableProperty] private string _guestAddress = string.Empty;
    [ObservableProperty] private int _guestPort;

    public PortForward ToModel() => new(Protocol, HostAddress.Trim(), HostPort, GuestAddress.Trim(), GuestPort);

    public static PortForwardViewModel From(PortForward rule) => new()
    {
        Protocol = rule.Protocol, HostAddress = rule.HostAddress, HostPort = rule.HostPort,
        GuestAddress = rule.GuestAddress, GuestPort = rule.GuestPort
    };
}

/// <summary>
/// The dialog behind both "Add adapter" and "Edit". Adding walks three steps (type, details, summary);
/// editing opens straight on the details of the adapter's own type, which cannot be changed, so the form
/// only ever shows the settings that type has.
/// </summary>
public partial class NetworkAdapterViewModel : ViewModelBase
{
    private const int LastStep = 2;
    private static readonly Regex MacPattern = new("^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$");

    private static readonly string[] DerivedProperties =
    [
        nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(CanGoBack), nameof(IsLastStep), nameof(ShowNext),
        nameof(IsUser), nameof(IsTap), nameof(IsBridge), nameof(IsSocket),
        nameof(IsSocketListen), nameof(IsSocketConnect), nameof(IsSocketMulticast), nameof(IsSocketUdp),
        nameof(ValidationMessage), nameof(HasValidationMessage), nameof(CanGoNext), nameof(Warning), nameof(HasWarning),
        nameof(CommandPreview), nameof(Summary), nameof(StepTitle), nameof(DialogTitle), nameof(BackendDescription),
        nameof(SocketHostLabel), nameof(CanEditType), nameof(Model), nameof(ModelDescription)
    ];

    public static IReadOnlyList<BackendChoice> Backends { get; } =
    [
        new(NetworkBackendType.User, "NAT (user networking)",
            "The guest reaches the outside through this computer. Needs no privileges; forward host ports to reach services in the guest."),
        new(NetworkBackendType.Tap, "Tap interface",
            "Attach to a virtual network interface that already exists on this computer."),
        new(NetworkBackendType.Bridge, "Bridge",
            "Join the guest to an existing network bridge so it appears on your LAN (Linux)."),
        new(NetworkBackendType.Socket, "Socket (machine to machine)",
            "Connect machines to each other over TCP, UDP or multicast, with no host network involved.")
    ];

    public IReadOnlyList<BackendChoice> BackendChoices => Backends;
    public IReadOnlyList<NetworkModelInfo> ModelChoices => NetworkModelCatalog.All;
    public NetworkSocketMode[] SocketModes { get; } = Enum.GetValues<NetworkSocketMode>();
    public string[] Protocols { get; } = ["tcp", "udp"];

    public ObservableCollection<PortForwardViewModel> Forwards { get; } = [];

    [ObservableProperty] private int _step;
    [ObservableProperty] private BackendChoice _selectedBackend = Backends[0];
    [ObservableProperty] private NetworkModelInfo _selectedModel = NetworkModelCatalog.Get(NetworkModel.VirtIoNetPci);
    [ObservableProperty] private string _macAddress = string.Empty;

    [ObservableProperty] private string _userSubnet = string.Empty;
    [ObservableProperty] private string _userDhcpStart = string.Empty;
    [ObservableProperty] private string _userDns = string.Empty;
    [ObservableProperty] private string _userHostname = string.Empty;
    [ObservableProperty] private bool _userRestrict;

    [ObservableProperty] private string _tapInterface = string.Empty;
    [ObservableProperty] private string _tapScript = string.Empty;
    [ObservableProperty] private string _tapDownScript = string.Empty;

    [ObservableProperty] private string _bridgeInterface = string.Empty;

    [ObservableProperty] private NetworkSocketMode _socketMode = NetworkSocketMode.Listen;
    [ObservableProperty] private string _socketHost = string.Empty;
    [ObservableProperty] private int _socketPort = 1234;
    [ObservableProperty] private string _socketLocalHost = "127.0.0.1";
    [ObservableProperty] private int _socketLocalPort = 1235;

    [ObservableProperty] private string _statusMessage = string.Empty;

    /// <summary>Set by the window: opens a file picker (for the tap scripts).</summary>
    public Func<bool, string, Task<string?>>? FilePicker { get; set; }

    public bool IsEditing { get; private set; }

    /// <summary>The selected card as the enum the entity stores.</summary>
    public NetworkModel Model
    {
        get => SelectedModel.Model;
        set => SelectedModel = NetworkModelCatalog.Get(value);
    }

    /// <summary>What the selected card is and when to pick it, shown under the field.</summary>
    public string ModelDescription => SelectedModel.Description;
    public NetworkAdapter? Result { get; private set; }

    public event EventHandler? CloseRequested;

    private NetworkAdapter _original = new();

    public NetworkAdapterViewModel()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || Array.IndexOf(DerivedProperties, e.PropertyName) >= 0)
                return;
            RaiseDerived();
        };
        Forwards.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (PortForwardViewModel item in e.NewItems)
                    item.PropertyChanged += (_, _) => RaiseDerived();
            RaiseDerived();
        };
    }

    // ---- entry points ----

    /// <summary>Starts the wizard for a new adapter, with a MAC that does not clash with the machine's others.</summary>
    public void InitializeForAdd(IEnumerable<NetworkAdapter> existing)
    {
        IsEditing = false;
        _original = new NetworkAdapter();
        Step = 0;
        var taken = existing.Select(n => n.MacAddress.ToLowerInvariant()).ToHashSet();
        string mac;
        do { mac = GenerateMac(); } while (taken.Contains(mac));
        MacAddress = mac;
        RaiseDerived();
    }

    /// <summary>Opens the details of an existing adapter. Its type is fixed.</summary>
    public void InitializeForEdit(NetworkAdapter adapter)
    {
        IsEditing = true;
        _original = adapter;
        SelectedBackend = Backends.First(b => b.Type == adapter.BackendType);
        Model = adapter.Model;
        MacAddress = adapter.MacAddress;

        UserSubnet = adapter.UserSubnet;
        UserDhcpStart = adapter.UserDhcpStart;
        UserDns = adapter.UserDns;
        UserHostname = adapter.UserHostname;
        UserRestrict = adapter.UserRestrict;
        Forwards.Clear();
        foreach (var rule in PortForwardRules.Parse(adapter.HostForwardingRules))
            Forwards.Add(PortForwardViewModel.From(rule));

        TapInterface = adapter.TapInterface;
        TapScript = adapter.TapScript;
        TapDownScript = adapter.TapDownScript;
        BridgeInterface = adapter.BridgeInterface;

        SocketMode = adapter.SocketMode;
        SocketHost = adapter.SocketHost;
        SocketPort = adapter.SocketPort;
        SocketLocalHost = adapter.SocketLocalHost;
        SocketLocalPort = adapter.SocketLocalPort;

        Step = 1;
        RaiseDerived();
    }

    /// <summary>A locally administered, unicast address in QEMU's own 52:54:00 range.</summary>
    public static string GenerateMac()
    {
        var bytes = new byte[3];
        Random.Shared.NextBytes(bytes);
        return $"52:54:00:{bytes[0]:x2}:{bytes[1]:x2}:{bytes[2]:x2}";
    }

    // ---- what the dialog shows ----

    public string DialogTitle => IsEditing ? $"Edit adapter - {SelectedBackend.Title}" : "Add network adapter";

    public string StepTitle => IsEditing ? "Settings" : Step switch
    {
        0 => "Step 1 of 3 · Type",
        1 => "Step 2 of 3 · Settings",
        _ => "Step 3 of 3 · Review"
    };

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool CanEditType => !IsEditing;
    public bool CanGoBack => !IsEditing && Step > 0;
    public bool IsLastStep => IsEditing || Step == LastStep;
    public bool ShowNext => !IsLastStep;

    public bool IsUser => SelectedBackend.Type == NetworkBackendType.User;
    public bool IsTap => SelectedBackend.Type == NetworkBackendType.Tap;
    public bool IsBridge => SelectedBackend.Type == NetworkBackendType.Bridge;
    public bool IsSocket => SelectedBackend.Type == NetworkBackendType.Socket;
    public bool IsSocketListen => IsSocket && SocketMode == NetworkSocketMode.Listen;
    public bool IsSocketConnect => IsSocket && SocketMode == NetworkSocketMode.Connect;
    public bool IsSocketMulticast => IsSocket && SocketMode == NetworkSocketMode.Multicast;
    public bool IsSocketUdp => IsSocket && SocketMode == NetworkSocketMode.Udp;

    public string BackendDescription => SelectedBackend.Description;

    public string SocketHostLabel => SocketMode switch
    {
        NetworkSocketMode.Multicast => "Multicast group",
        NetworkSocketMode.Udp => "Peer address",
        NetworkSocketMode.Connect => "Host to connect to",
        _ => "Address to listen on (optional)"
    };

    public string? ValidationMessage => Validate();
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool CanGoNext => Step != 1 || ValidationMessage is null;

    /// <summary>Something that will probably not work here. A warning, not a block: the QEMU that runs it may differ.</summary>
    public string Warning
    {
        get
        {
            if (IsBridge && !OperatingSystem.IsLinux())
                return "Bridge networking is only built into Linux versions of QEMU. This computer's QEMU will most likely reject it.";
            if (IsTap && OperatingSystem.IsWindows())
                return "On Windows, tap needs a TAP-Windows adapter, and the interface name is that adapter's name.";
            if (IsSocketMulticast && OperatingSystem.IsWindows())
                return "QEMU on Windows cannot bind a multicast group (\"can't bind ip to socket\"). Use listen and connect instead.";
            return string.Empty;
        }
    }

    public bool HasWarning => Warning.Length > 0;

    public string CommandPreview
    {
        get
        {
            if (Validate() is not null) return string.Empty;
            var args = QemuArgumentBuilder.NetworkArguments(BuildAdapter(), 0);
            return string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        }
    }

    public string Summary
    {
        get
        {
            var adapter = BuildAdapter();
            var mac = adapter.MacAddress.Length > 0 ? adapter.MacAddress : "assigned by QEMU";
            return $"{SelectedBackend.Title}, {adapter.Summary}\n{adapter.Model} · MAC {mac}";
        }
    }

    // ---- validation ----

    private string? Validate()
    {
        if (MacAddress.Trim().Length > 0)
        {
            if (!MacPattern.IsMatch(MacAddress.Trim()))
                return "The MAC address must look like 52:54:00:12:34:56.";
            if ((Convert.ToInt32(MacAddress.Trim()[..2], 16) & 1) == 1)
                return "A MAC address with its first bit set is a multicast address; use an even first byte.";
        }

        switch (SelectedBackend.Type)
        {
            case NetworkBackendType.User:
                if (UserSubnet.Trim().Length > 0 && !IsIPv4WithOptionalMask(UserSubnet.Trim()))
                    return "The network must be an IPv4 address with an optional mask, like 10.0.2.0/24.";
                if (UserDhcpStart.Trim().Length > 0 && !IsIPv4(UserDhcpStart))
                    return "The first DHCP address must be an IPv4 address.";
                if (UserDns.Trim().Length > 0 && !IsIPv4(UserDns))
                    return "The DNS server must be an IPv4 address.";
                if (UserHostname.Trim().Length > 0 && !Regex.IsMatch(UserHostname.Trim(), "^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$"))
                    return "The host name may only hold letters, digits, dots and hyphens.";
                return ValidateForwards();

            case NetworkBackendType.Tap:
                if (TapScript.Trim().Length > 0 && !File.Exists(TapScript.Trim()))
                    return "The up script does not exist.";
                if (TapDownScript.Trim().Length > 0 && !File.Exists(TapDownScript.Trim()))
                    return "The down script does not exist.";
                return null;

            case NetworkBackendType.Bridge:
                return null;

            case NetworkBackendType.Socket:
                return ValidateSocket();
        }

        return null;
    }

    private string? ValidateForwards()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < Forwards.Count; i++)
        {
            var f = Forwards[i];
            var n = i + 1;
            if (f.Protocol is not ("tcp" or "udp"))
                return $"Forward {n}: the protocol must be tcp or udp.";
            if (f.HostPort is < 1 or > 65535)
                return $"Forward {n}: the host port must be between 1 and 65535.";
            if (f.GuestPort is < 1 or > 65535)
                return $"Forward {n}: the guest port must be between 1 and 65535.";
            if (f.HostAddress.Trim().Length > 0 && !IsIPv4(f.HostAddress))
                return $"Forward {n}: the host address must be an IPv4 address, or empty for all.";
            if (f.GuestAddress.Trim().Length > 0 && !IsIPv4(f.GuestAddress))
                return $"Forward {n}: the guest address must be an IPv4 address, or empty.";
            if (!seen.Add($"{f.Protocol}:{f.HostAddress.Trim()}:{f.HostPort}"))
                return $"Forward {n}: another rule already uses {f.Protocol} port {f.HostPort} on the host.";
        }

        return null;
    }

    private string? ValidateSocket()
    {
        if (SocketPort is < 1 or > 65535)
            return "The port must be between 1 and 65535.";

        switch (SocketMode)
        {
            case NetworkSocketMode.Connect:
                if (SocketHost.Trim().Length == 0) return "Enter the host to connect to.";
                break;
            case NetworkSocketMode.Multicast:
                if (!IPAddress.TryParse(SocketHost.Trim(), out var group) || group.AddressFamily != AddressFamily.InterNetwork
                    || group.GetAddressBytes()[0] is < 224 or > 239)
                    return "The multicast group must be an IPv4 address from 224.0.0.0 to 239.255.255.255.";
                break;
            case NetworkSocketMode.Udp:
                if (SocketHost.Trim().Length == 0) return "Enter the peer's address.";
                if (SocketLocalHost.Trim().Length == 0) return "Enter the local address to listen on.";
                if (SocketLocalPort is < 1 or > 65535) return "The local port must be between 1 and 65535.";
                break;
        }

        return null;
    }

    private static bool IsIPv4(string text)
        => IPAddress.TryParse(text.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && text.Trim().Count(c => c == '.') == 3;

    private static bool IsIPv4WithOptionalMask(string text)
    {
        var parts = text.Split('/');
        if (parts.Length > 2 || !IsIPv4(parts[0])) return false;
        if (parts.Length == 1) return true;
        return int.TryParse(parts[1], out var bits) ? bits is >= 1 and <= 30 : IsIPv4(parts[1]);
    }

    // ---- building the result ----

    private NetworkAdapter BuildAdapter()
    {
        var adapter = new NetworkAdapter
        {
            Id = _original.Id,
            VmId = _original.VmId,
            BackendType = SelectedBackend.Type,
            Model = Model,
            MacAddress = MacAddress.Trim()
        };

        switch (SelectedBackend.Type)
        {
            case NetworkBackendType.User:
                adapter.UserSubnet = UserSubnet.Trim();
                adapter.UserDhcpStart = UserDhcpStart.Trim();
                adapter.UserDns = UserDns.Trim();
                adapter.UserHostname = UserHostname.Trim();
                adapter.UserRestrict = UserRestrict;
                adapter.HostForwardingRules = PortForwardRules.ToJson(Forwards.Select(f => f.ToModel()));
                break;
            case NetworkBackendType.Tap:
                adapter.TapInterface = TapInterface.Trim();
                adapter.TapScript = TapScript.Trim();
                adapter.TapDownScript = TapDownScript.Trim();
                break;
            case NetworkBackendType.Bridge:
                adapter.BridgeInterface = BridgeInterface.Trim();
                break;
            case NetworkBackendType.Socket:
                adapter.SocketMode = SocketMode;
                adapter.SocketHost = SocketHost.Trim();
                adapter.SocketPort = SocketPort;
                adapter.SocketLocalHost = SocketLocalHost.Trim();
                adapter.SocketLocalPort = SocketLocalPort;
                break;
        }

        return adapter;
    }

    private void RaiseDerived()
    {
        foreach (var name in DerivedProperties)
            OnPropertyChanged(name);
    }

    // ---- commands ----

    [RelayCommand]
    private void Next()
    {
        if (Step == 1 && Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        StatusMessage = string.Empty;
        if (Step < LastStep) Step++;
    }

    [RelayCommand]
    private void Back()
    {
        StatusMessage = string.Empty;
        if (Step > 0 && !IsEditing) Step--;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Finish()
    {
        if (Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        Result = BuildAdapter();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GenerateMacAddress() => MacAddress = GenerateMac();

    [RelayCommand]
    private void AddForward() => Forwards.Add(new PortForwardViewModel { HostAddress = "127.0.0.1", HostPort = 2222, GuestPort = 22 });

    [RelayCommand]
    private void RemoveForward(PortForwardViewModel? forward)
    {
        if (forward is not null) Forwards.Remove(forward);
    }

    [RelayCommand]
    private async Task BrowseTapScriptAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(false, "Choose the up script");
        if (!string.IsNullOrEmpty(picked)) TapScript = picked;
    }

    [RelayCommand]
    private async Task BrowseTapDownScriptAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(false, "Choose the down script");
        if (!string.IsNullOrEmpty(picked)) TapDownScript = picked;
    }
}
