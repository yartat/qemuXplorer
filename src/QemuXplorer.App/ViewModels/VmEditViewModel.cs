using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using System.Collections.ObjectModel;

namespace QemuXplorer.App.ViewModels;

public partial class VmEditViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
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

    // Tracked explicitly: inferring it from CreatedAt == ModifiedAt reports "new" for a
    // freshly persisted VM and makes Save insert it a second time.
    private bool _isNew = true;

    public bool IsNew => _isNew;

    // Resolved once per dialog so the preview can refresh on every keystroke without
    // hitting the database.
    private QemuInstallation? _qemu;
    private bool _suppressPreview;

    public string ArchSummary => $"{Architecture} · {MachineType}";

    /// <summary>Machines the selected architecture's emulator accepts. Refilters on change.</summary>
    public ObservableCollection<MachineTypeInfo> MachineTypes { get; } = [];

    /// <summary>
    /// The picker's selection. Null whenever <see cref="MachineType"/> holds something the
    /// catalog does not know — a versioned variant such as pc-q35-9.2, or a machine from a QEMU
    /// build we have no list for. Setting it writes the QEMU name through.
    /// </summary>
    public MachineTypeInfo? SelectedMachineType
    {
        get => MachineCatalog.Find(MachineType);
        set
        {
            if (value is not null && value.QemuName != MachineType)
                MachineType = value.QemuName;
        }
    }

    /// <summary>Set when MachineType is not a machine this architecture is known to accept.</summary>
    public bool HasUnknownMachineType => !MachineCatalog.IsValidFor(MachineType, Architecture);

    public string MachineTypeWarning => MachineCatalog.Find(MachineType) is null
        ? $"\"{MachineType}\" is not in the catalog. It may still be valid — versioned names such "
          + "as pc-q35-9.2 are accepted by QEMU but deliberately not listed."
        : $"\"{MachineType}\" is not offered by the {Architecture} emulator.";

    /// <param name="autoCorrect">
    /// True only when the user changed architecture, where snapping to that architecture's
    /// default is helpful. False when loading a saved machine: silently rewriting a stored
    /// value on open would hide the problem instead of showing it.
    /// </param>
    private void RebuildMachineTypes(bool autoCorrect)
    {
        MachineTypes.Clear();
        foreach (var machine in MachineCatalog.ForArchitecture(Architecture))
            MachineTypes.Add(machine);

        if (autoCorrect && !MachineCatalog.IsValidFor(MachineType, Architecture))
        {
            var fallback = MachineCatalog.DefaultFor(Architecture);
            if (fallback is not null)
                MachineType = fallback.QemuName;
        }

        OnPropertyChanged(nameof(SelectedMachineType));
        OnPropertyChanged(nameof(HasUnknownMachineType));
        OnPropertyChanged(nameof(MachineTypeWarning));
    }

    public string VcpuSummary => $"= {CpuSockets * CpuCores * CpuThreads} vCPU";

    public event EventHandler? CloseRequested;

    public VmEditViewModel(IServiceScopeFactory scopeFactory, IQemuArgumentBuilder argBuilder)
    {
        _scopeFactory = scopeFactory;
        _argBuilder = argBuilder;
        _vm = new VirtualMachine();

        PropertyChanged += (_, e) =>
        {
            if (_suppressPreview) return;
            // Computed properties are raised from here; re-entering for them would rebuild the
            // preview several times per keystroke.
            if (e.PropertyName is nameof(GeneratedCommand) or nameof(ArchSummary) or nameof(VcpuSummary)
                or nameof(SelectedMachineType) or nameof(HasUnknownMachineType) or nameof(MachineTypeWarning))
                return;

            if (e.PropertyName is nameof(Architecture) or nameof(MachineType))
                OnPropertyChanged(nameof(ArchSummary));

            if (e.PropertyName is nameof(Architecture))
                RebuildMachineTypes(autoCorrect: true);

            if (e.PropertyName is nameof(MachineType))
            {
                OnPropertyChanged(nameof(SelectedMachineType));
                OnPropertyChanged(nameof(HasUnknownMachineType));
                OnPropertyChanged(nameof(MachineTypeWarning));
            }
            if (e.PropertyName is nameof(CpuSockets) or nameof(CpuCores) or nameof(CpuThreads))
                OnPropertyChanged(nameof(VcpuSummary));

            RefreshPreview();
        };
    }

    public async Task LoadVmAsync(VirtualMachine vm, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        _qemu = await vmService.GetDefaultInstallationAsync(ct);
        LoadVm(vm);
        RefreshPreview();
    }

    public void LoadVm(VirtualMachine vm)
    {
        _vm = vm;
        _isNew = false;
        _suppressPreview = true;

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

        RebuildMachineTypes(autoCorrect: false);

        _suppressPreview = false;
        OnPropertyChanged(nameof(ArchSummary));
        OnPropertyChanged(nameof(VcpuSummary));
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
        using var scope = _scopeFactory.CreateScope();
        var vmService = scope.ServiceProvider.GetRequiredService<VirtualMachineService>();
        if (_isNew)
        {
            await vmService.AddAsync(_vm);
            _isNew = false;
        }
        else
        {
            await vmService.UpdateAsync(_vm);
        }

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

    /// <summary>
    /// Rebuilds the preview from the in-memory edits. Synchronous on purpose: it runs on every
    /// property change, so it must not touch the database.
    /// </summary>
    private void RefreshPreview()
    {
        if (_qemu is null)
        {
            GeneratedCommand = "No default QEMU installation is configured. "
                             + "Set one on the Installations page to preview the command.";
            return;
        }

        try
        {
            ApplyToVm();
            var launchArgs = _argBuilder.Build(_vm, _qemu);
            GeneratedCommand = string.Join(" ", new[] { launchArgs.Binary }
                .Concat(launchArgs.Arguments)
                .Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        }
        catch (Exception ex)
        {
            GeneratedCommand = $"Error: {ex.Message}";
        }
    }
}
