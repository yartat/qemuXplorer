using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.App.ViewModels;

public enum DiskSourceMode
{
    CreateNew,
    ExistingFile,
    Nbd,
    Ssh,
    Iscsi
}

public sealed record SourceChoice(DiskSourceMode Mode, string Title, string Description);

/// <summary>
/// The "Add disk" wizard. Step 0 picks where the disk comes from, step 1 collects what that
/// source needs (qemu-img options, a file, or connection details), step 2 sets how it attaches.
/// Finishing yields a <see cref="DiskDevice"/>; creating a new image runs qemu-img first.
/// </summary>
public partial class DiskWizardViewModel : ViewModelBase
{
    private const int LastStep = 2;

    private static readonly string[] DerivedProperties =
    [
        nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(CanGoBack), nameof(IsLastStep),
        nameof(IsCreate), nameof(IsExisting), nameof(IsNbd), nameof(IsSsh), nameof(IsIscsi),
        nameof(IsRemote), nameof(IsLocal), nameof(NeedsKey), nameof(FormatIsReadOnly),
        nameof(ShowOptions), nameof(ValidationMessage), nameof(HasValidationMessage),
        nameof(CanGoNext), nameof(DriverWarning), nameof(HasDriverWarning),
        nameof(CommandPreview), nameof(HasCommandPreview), nameof(Summary), nameof(FormatDescription),
        nameof(StepTitle), nameof(CanEditReadOnly), nameof(Mode)
    ];

    private readonly DiskImageService _images;
    private QemuInstallation? _qemu;
    private IReadOnlySet<string>? _drivers;

    public static IReadOnlyList<SourceChoice> Sources { get; } =
    [
        new(DiskSourceMode.CreateNew, "Create a new image",
            "Run qemu-img to make an empty disk: qcow2, qcow, qed, raw, vdi, vmdk, vpc, vhdx or luks."),
        new(DiskSourceMode.ExistingFile, "Use an existing image file",
            "Attach a disk file already on this machine, including read-only bochs, cloop, dmg and parallels."),
        new(DiskSourceMode.Nbd, "NBD server",
            "A disk exported over the Network Block Device protocol."),
        new(DiskSourceMode.Ssh, "File over SSH",
            "A disk image read directly from a remote host."),
        new(DiskSourceMode.Iscsi, "iSCSI LUN",
            "A block device from an iSCSI target.")
    ];

    // Instance properties over static data: compiled bindings resolve against the instance.
    public IReadOnlyList<SourceChoice> SourceChoices => Sources;
    public DiskInterface[] Interfaces { get; } = Enum.GetValues<DiskInterface>();
    public DiskCacheMode[] CacheModes { get; } = Enum.GetValues<DiskCacheMode>();
    public string[] SizeUnits { get; } = ["MB", "GB", "TB"];

    public ObservableCollection<DiskFormatInfo> AvailableFormats { get; } = [];
    public ObservableCollection<ImageOptionViewModel> Options { get; } = [];

    [ObservableProperty] private int _step;
    [ObservableProperty] private SourceChoice _selectedSource;
    [ObservableProperty] private DiskFormatInfo? _selectedFormat;

    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private decimal _sizeValue = 20;
    [ObservableProperty] private string _sizeUnit = "GB";
    [ObservableProperty] private string _keySecretFile = string.Empty;

    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _export = string.Empty;
    [ObservableProperty] private string _user = string.Empty;
    [ObservableProperty] private string _remotePath = string.Empty;
    [ObservableProperty] private string _target = string.Empty;
    [ObservableProperty] private int _lun = 1;

    [ObservableProperty] private DiskInterface _selectedInterface = DiskInterface.VirtIo;
    [ObservableProperty] private DiskCacheMode _selectedCache = DiskCacheMode.WriteBack;
    [ObservableProperty] private bool _isBoot;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _isCdRom;
    [ObservableProperty] private bool _isSnapshot;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>Set by the window: opens a file picker (save mode for a new image).</summary>
    public Func<bool, string, Task<string?>>? FilePicker { get; set; }

    public DiskDevice? Result { get; private set; }

    public event EventHandler? CloseRequested;

    public DiskWizardViewModel(DiskImageService images)
    {
        _images = images;
        _selectedSource = Sources[0];

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || Array.IndexOf(DerivedProperties, e.PropertyName) >= 0)
                return;
            RaiseDerived();
        };

        ApplySource(Sources[0]);
    }

    public DiskSourceMode Mode => SelectedSource.Mode;

    public string StepTitle => Step switch
    {
        0 => "Step 1 of 3 · Source",
        1 => "Step 2 of 3 · Details",
        _ => "Step 3 of 3 · Attach"
    };

    /// <summary>A read-only format is always attached read-only, so the box is locked on.</summary>
    public bool CanEditReadOnly => !FormatIsReadOnly;

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool CanGoBack => Step > 0 && !IsBusy;
    public bool IsLastStep => Step == LastStep;

    public bool IsCreate => Mode == DiskSourceMode.CreateNew;
    public bool IsExisting => Mode == DiskSourceMode.ExistingFile;
    public bool IsNbd => Mode == DiskSourceMode.Nbd;
    public bool IsSsh => Mode == DiskSourceMode.Ssh;
    public bool IsIscsi => Mode == DiskSourceMode.Iscsi;
    public bool IsLocal => IsCreate || IsExisting;
    public bool IsRemote => !IsLocal;

    public bool NeedsKey => SelectedFormat?.NeedsKeySecret ?? false;
    public bool FormatIsReadOnly => SelectedFormat?.IsReadOnly ?? false;
    public bool ShowOptions => IsCreate && Options.Count > 0;
    public string FormatDescription => SelectedFormat?.Description ?? string.Empty;

    public string? ValidationMessage => Validate();
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool CanGoNext => !IsBusy && (Step != 1 || ValidationMessage is null);

    /// <summary>
    /// Warns when the chosen network source needs a block driver the installed QEMU lacks. A
    /// warning rather than a block: the list comes from one binary and the user may target another.
    /// </summary>
    public string DriverWarning
    {
        get
        {
            var driver = DiskSourceUri.DriverFor(SourceKind);
            if (driver is null || _drivers is null || _drivers.Contains(driver))
                return string.Empty;
            return $"This QEMU build has no '{driver}' driver, so the disk will not open here. "
                 + "Other builds (often Linux or macOS packages) may include it.";
        }
    }

    public bool HasDriverWarning => DriverWarning.Length > 0;

    public string CommandPreview
    {
        get
        {
            if (!IsCreate || _qemu is null || string.IsNullOrWhiteSpace(Path))
                return string.Empty;
            try
            {
                var args = DiskImageService.BuildCreateArguments(BuildCreateRequest());
                return "qemu-img " + string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    public bool HasCommandPreview => CommandPreview.Length > 0;

    public string Summary
    {
        get
        {
            if (SelectedFormat is null) return string.Empty;
            var verb = IsCreate ? "Will create and attach" : "Will attach";
            return $"{verb} {ResolveFilePath()}  [{SelectedFormat.QemuName}, {SelectedInterface}]"
                 + (FormatIsReadOnly ? "  (read-only format)" : string.Empty);
        }
    }

    private DiskSourceKind SourceKind => Mode switch
    {
        DiskSourceMode.Nbd => DiskSourceKind.Nbd,
        DiskSourceMode.Ssh => DiskSourceKind.Ssh,
        DiskSourceMode.Iscsi => DiskSourceKind.Iscsi,
        _ => DiskSourceKind.LocalFile
    };

    public long SizeMb
    {
        get
        {
            var factor = SizeUnit switch { "TB" => 1024L * 1024, "GB" => 1024L, _ => 1L };
            return (long)Math.Round(SizeValue * factor);
        }
    }

    /// <summary>Called once the dialog is built: resolves qemu-img and probes its drivers.</summary>
    public async Task InitializeAsync(QemuInstallation? qemu, bool isFirstDisk)
    {
        _qemu = qemu;
        IsBoot = isFirstDisk;
        _drivers = null;

        if (qemu is not null)
        {
            try
            {
                _drivers = await _images.GetSupportedDriversAsync(DiskImageService.ResolveQemuImg(qemu.BinaryDirectory));
            }
            catch (Exception)
            {
                // No driver list just means no early warning; it must not stop the wizard.
                _drivers = null;
            }
        }

        RaiseDerived();
    }

    partial void OnSelectedSourceChanged(SourceChoice value) => ApplySource(value);

    partial void OnSelectedFormatChanged(DiskFormatInfo? value)
    {
        foreach (var option in Options)
            option.PropertyChanged -= OnOptionChanged;
        Options.Clear();

        if (value is not null)
        {
            foreach (var definition in value.CreateOptions)
            {
                var option = new ImageOptionViewModel(definition);
                option.PropertyChanged += OnOptionChanged;
                Options.Add(option);
            }
        }

        if (value?.IsReadOnly == true)
            IsReadOnly = true;
    }

    private void OnOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseDerived();

    private void ApplySource(SourceChoice choice)
    {
        Port = choice.Mode switch
        {
            DiskSourceMode.Nbd => DiskSourceUri.NbdDefaultPort,
            DiskSourceMode.Ssh => DiskSourceUri.SshDefaultPort,
            DiskSourceMode.Iscsi => DiskSourceUri.IscsiDefaultPort,
            _ => 0
        };

        AvailableFormats.Clear();
        foreach (var format in choice.Mode == DiskSourceMode.CreateNew ? DiskFormatCatalog.Creatable : DiskFormatCatalog.All)
            AvailableFormats.Add(format);

        // Remote disks are usually exported as raw block devices; local images default to qcow2.
        var preferred = choice.Mode is DiskSourceMode.Nbd or DiskSourceMode.Ssh or DiskSourceMode.Iscsi
            ? DiskFormat.Raw
            : DiskFormat.QCow2;
        SelectedFormat = AvailableFormats.First(f => f.Format == preferred);

        RaiseDerived();
    }

    private void RaiseDerived()
    {
        foreach (var name in DerivedProperties)
            OnPropertyChanged(name);
    }

    private string? Validate()
    {
        var format = SelectedFormat;
        if (format is null)
            return "Choose a format.";

        switch (Mode)
        {
            case DiskSourceMode.CreateNew:
                if (_qemu is null)
                    return "No default QEMU installation. It supplies the qemu-img that creates the image; set one on the Installations page.";
                if (string.IsNullOrWhiteSpace(Path))
                    return "Choose where to create the image.";
                if (!FolderExists(Path))
                    return "The folder for the new image does not exist.";
                if (File.Exists(Path.Trim()))
                    return "A file with that name already exists. It will not be overwritten.";
                if (SizeMb < 1)
                    return "The size must be at least 1 MB.";
                break;

            case DiskSourceMode.ExistingFile:
                if (string.IsNullOrWhiteSpace(Path))
                    return "Choose an image file.";
                if (!File.Exists(Path.Trim()))
                    return "That file does not exist.";
                break;

            case DiskSourceMode.Nbd:
            case DiskSourceMode.Ssh:
            case DiskSourceMode.Iscsi:
                if (string.IsNullOrWhiteSpace(Host))
                    return "Enter the server host name or address.";
                if (Port is < 1 or > 65535)
                    return "The port must be between 1 and 65535.";
                if (IsSsh && string.IsNullOrWhiteSpace(RemotePath))
                    return "Enter the absolute path of the image on the server.";
                if (IsIscsi && string.IsNullOrWhiteSpace(Target))
                    return "Enter the iSCSI target name (IQN).";
                if (IsIscsi && Lun < 0)
                    return "The LUN cannot be negative.";
                break;
        }

        if (format.NeedsKeySecret && (string.IsNullOrWhiteSpace(KeySecretFile) || !File.Exists(KeySecretFile.Trim())))
            return "Choose the file that holds the LUKS passphrase.";

        return null;
    }

    private static bool FolderExists(string path)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path.Trim()));
            return !string.IsNullOrEmpty(directory) && Directory.Exists(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private string ResolveFilePath() => Mode switch
    {
        DiskSourceMode.Nbd => DiskSourceUri.Nbd(Host, Port, Export),
        DiskSourceMode.Ssh => DiskSourceUri.Ssh(User, Host, Port, RemotePath),
        DiskSourceMode.Iscsi => DiskSourceUri.Iscsi(User, Host, Port, Target, Lun),
        _ => Path.Trim()
    };

    private DiskConnection? BuildConnection()
    {
        var key = NeedsKey ? KeySecretFile.Trim() : string.Empty;
        if (Mode is DiskSourceMode.CreateNew or DiskSourceMode.ExistingFile)
            return key.Length == 0 ? null : new DiskConnection { KeySecretFile = key };

        return new DiskConnection
        {
            Kind = SourceKind,
            Host = Host.Trim(),
            Port = Port,
            User = IsNbd ? string.Empty : User.Trim(),
            Export = IsNbd ? Export.Trim() : string.Empty,
            RemotePath = IsSsh ? RemotePath.Trim() : string.Empty,
            Target = IsIscsi ? Target.Trim() : string.Empty,
            Lun = IsIscsi ? Lun : 0,
            KeySecretFile = key
        };
    }

    private DiskCreateRequest BuildCreateRequest()
    {
        var options = Options.Where(o => o.IsSet).ToDictionary(o => o.Definition.Key, o => o.Value);
        return new DiskCreateRequest(
            Path.Trim(),
            SelectedFormat!.Format,
            SizeMb,
            options,
            NeedsKey ? KeySecretFile.Trim() : null);
    }

    private DiskDevice BuildDevice() => new()
    {
        FilePath = ResolveFilePath(),
        Format = SelectedFormat!.Format,
        Interface = SelectedInterface,
        CacheMode = SelectedCache,
        IsBootDevice = IsBoot,
        IsReadOnly = IsReadOnly || FormatIsReadOnly,
        IsCdRom = IsCdRom,
        IsSnapshotMode = IsSnapshot,
        ConnectionJson = BuildConnection()?.ToJson() ?? string.Empty
    };

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
        if (Step > 0) Step--;
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        IsBusy = true;
        StatusMessage = IsCreate ? "Creating the image..." : string.Empty;
        try
        {
            if (IsCreate)
            {
                await _images.CreateImageAsync(
                    DiskImageService.ResolveQemuImg(_qemu!.BinaryDirectory),
                    BuildCreateRequest());
            }

            Result = BuildDevice();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    [RelayCommand]
    private async Task BrowsePathAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(IsCreate, IsCreate ? "Create image as" : "Choose an image");
        if (!string.IsNullOrEmpty(picked)) Path = picked;
    }

    [RelayCommand]
    private async Task BrowseKeyAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(false, "Choose the passphrase file");
        if (!string.IsNullOrEmpty(picked)) KeySecretFile = picked;
    }
}
