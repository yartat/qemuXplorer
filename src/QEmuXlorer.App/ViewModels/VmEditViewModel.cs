using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Models.Entities;
using QEmuXlorer.Models.Enums;

namespace QEmuXlorer.App.ViewModels;

public partial class VmEditViewModel : ViewModelBase
{
    private readonly VirtualMachineService _vmService;
    private readonly IQemuArgumentBuilder _argBuilder;
    private VirtualMachine _vm;

    // ── General ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private GuestArchitecture _architecture;
    [ObservableProperty] private string _machineType = "q35";
    [ObservableProperty] private bool _isTemplate;

    // ── CPU ───────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _cpuModel = "host";
    [ObservableProperty] private int _cpuCount = 1;
    [ObservableProperty] private int _cpuSockets = 1;
    [ObservableProperty] private int _cpuCores = 1;
    [ObservableProperty] private int _cpuThreads = 1;

    // ── Memory ────────────────────────────────────────────────────────────────
    [ObservableProperty] private long _memorySizeMB = 1024;

    // ── Accelerators ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _enableKvm;
    [ObservableProperty] private bool _enableHvf;
    [ObservableProperty] private bool _enableWhpx;
    [ObservableProperty] private bool _fallbackToTcg = true;

    // ── Display ───────────────────────────────────────────────────────────────
    [ObservableProperty] private DisplayType _displayType;
    [ObservableProperty] private VgaType _vgaType = VgaType.Std;
    [ObservableProperty] private int _vncPort = 5900;
    [ObservableProperty] private int _spicePort = 5930;
    [ObservableProperty] private bool _isFullscreen;

    // ── Advanced ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _extraArguments = string.Empty;
    [ObservableProperty] private string _generatedCommand = string.Empty;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<DiskDevice> Disks { get; } = [];
    public ObservableCollection<NetworkAdapter> NetworkAdapters { get; } = [];
    public ObservableCollection<UsbDevice> UsbDevices { get; } = [];

    [ObservableProperty] private DiskDevice? _selectedDisk;
    [ObservableProperty] private NetworkAdapter? _selectedNetworkAdapter;
    [ObservableProperty] private UsbDevice? _selectedUsbDevice;

    // ── Enum lists for ComboBoxes ─────────────────────────────────────────────
    public static GuestArchitecture[] Architectures { get; } = Enum.GetValues<GuestArchitecture>();
    public static DisplayType[] DisplayTypes { get; } = Enum.GetValues<DisplayType>();
    public static VgaType[] VgaTypes { get; } = Enum.GetValues<VgaType>();
    public static DiskFormat[] DiskFormats { get; } = Enum.GetValues<DiskFormat>();
    public static DiskInterface[] DiskInterfaces { get; } = Enum.GetValues<DiskInterface>();
    public static DiskCacheMode[] DiskCacheModes { get; } = Enum.GetValues<DiskCacheMode>();
    public static NetworkBackendType[] NetworkBackends { get; } = Enum.GetValues<NetworkBackendType>();
    public static NetworkModel[] NetworkModels { get; } = Enum.GetValues<NetworkModel>();

    public bool IsNew => _vm.CreatedAt == _vm.ModifiedAt;

    public event EventHandler? CloseRequested;

    public VmEditViewModel(VirtualMachineService vmService, IQemuArgumentBuilder argBuilder)
    {
        _vmService = vmService;
        _argBuilder = argBuilder;
        _vm = new VirtualMachine();
    }

    public void LoadVm(VirtualMachine vm)
    {
        _vm = vm;

        Name = vm.Name;
        Description = vm.Description;
        Architecture = vm.Architecture;
        MachineType = vm.MachineType;
        IsTemplate = vm.IsTemplate;

        CpuModel = vm.Cpu.Model;
        CpuCount = vm.Cpu.Count;
        CpuSockets = vm.Cpu.Sockets;
        CpuCores = vm.Cpu.Cores;
        CpuThreads = vm.Cpu.Threads;

        MemorySizeMB = vm.Memory.SizeMB;

        EnableKvm = vm.EnableKvm;
        EnableHvf = vm.EnableHvf;
        EnableWhpx = vm.EnableWhpx;
        FallbackToTcg = vm.FallbackToTcg;

        DisplayType = vm.Display.Type;
        VgaType = vm.Display.VgaModel;
        VncPort = vm.Display.VncPort;
        SpicePort = vm.Display.SpicePort;
        IsFullscreen = vm.Display.IsFullscreen;

        ExtraArguments = vm.ExtraArguments;

        Disks.Clear();
        foreach (var d in vm.Disks) Disks.Add(d);
        NetworkAdapters.Clear();
        foreach (var n in vm.NetworkAdapters) NetworkAdapters.Add(n);
        UsbDevices.Clear();
        foreach (var u in vm.UsbDevices) UsbDevices.Add(u);
    }

    private void ApplyToVm()
    {
        _vm.Name = Name;
        _vm.Description = Description;
        _vm.Architecture = Architecture;
        _vm.MachineType = MachineType;
        _vm.IsTemplate = IsTemplate;

        _vm.Cpu.Model = CpuModel;
        _vm.Cpu.Count = CpuCount;
        _vm.Cpu.Sockets = CpuSockets;
        _vm.Cpu.Cores = CpuCores;
        _vm.Cpu.Threads = CpuThreads;

        _vm.Memory.SizeMB = MemorySizeMB;

        _vm.EnableKvm = EnableKvm;
        _vm.EnableHvf = EnableHvf;
        _vm.EnableWhpx = EnableWhpx;
        _vm.FallbackToTcg = FallbackToTcg;

        _vm.Display.Type = DisplayType;
        _vm.Display.VgaModel = VgaType;
        _vm.Display.VncPort = VncPort;
        _vm.Display.SpicePort = SpicePort;
        _vm.Display.IsFullscreen = IsFullscreen;

        _vm.ExtraArguments = ExtraArguments;

        _vm.Disks.Clear();
        foreach (var d in Disks) { d.VmId = _vm.Id; _vm.Disks.Add(d); }
        _vm.NetworkAdapters.Clear();
        foreach (var n in NetworkAdapters) { n.VmId = _vm.Id; _vm.NetworkAdapters.Add(n); }
        _vm.UsbDevices.Clear();
        foreach (var u in UsbDevices) { u.VmId = _vm.Id; _vm.UsbDevices.Add(u); }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ApplyToVm();
        if (IsNew)
            await _vmService.AddAsync(_vm);
        else
            await _vmService.UpdateAsync(_vm);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AddDisk()
    {
        Disks.Add(new DiskDevice { VmId = _vm.Id, Index = Disks.Count });
    }

    [RelayCommand]
    private void RemoveDisk(DiskDevice? disk)
    {
        if (disk is not null) Disks.Remove(disk);
    }

    [RelayCommand]
    private void AddNetworkAdapter()
    {
        NetworkAdapters.Add(new NetworkAdapter { VmId = _vm.Id });
    }

    [RelayCommand]
    private void RemoveNetworkAdapter(NetworkAdapter? adapter)
    {
        if (adapter is not null) NetworkAdapters.Remove(adapter);
    }

    [RelayCommand]
    private void AddUsbDevice()
    {
        UsbDevices.Add(new UsbDevice { VmId = _vm.Id });
    }

    [RelayCommand]
    private void RemoveUsbDevice(UsbDevice? device)
    {
        if (device is not null) UsbDevices.Remove(device);
    }

    [RelayCommand]
    private void UpdateGeneratedCommand()
    {
        ApplyToVm();
        // We need a QemuInstallation stub for preview
        var stubQemu = new QemuInstallation { BinaryDirectory = "/usr/bin" };
        try
        {
            var launchArgs = _argBuilder.Build(_vm, stubQemu);
            GeneratedCommand = $"{launchArgs.Binary} {string.Join(" ", launchArgs.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";
        }
        catch (Exception ex)
        {
            GeneratedCommand = $"Error: {ex.Message}";
        }
    }
}
