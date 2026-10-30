using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Dtos;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;

namespace QemuXplorer.App.ViewModels;

public enum DiskEditStage
{
    Editing,
    Confirming,
    Running,
    Done
}

/// <summary>
/// The "Edit disk" dialog. The top half is how the disk attaches to the machine; the rest changes the
/// image itself through qemu-img, limited to what that format supports: resize, convert, rebase,
/// amend, check. Nothing runs until the exact commands have been shown and confirmed.
/// </summary>
public partial class DiskEditViewModel : ViewModelBase
{
    private enum OperationKind { Check, Amend, Rebase, Resize, Convert }

    private sealed record Planned(OperationKind Kind, DiskImageOperation Operation);

    private static readonly string[] DerivedProperties =
    [
        nameof(IsEditing), nameof(IsConfirming), nameof(IsRunning), nameof(IsDone), nameof(CanEditSettings),
        nameof(ValidationMessage), nameof(HasValidationMessage), nameof(HasPlan), nameof(CommandPreview),
        nameof(WarningText), nameof(HasWarnings), nameof(IsShrinking), nameof(ShowPreallocation),
        nameof(CanEditReadOnly), nameof(NewSizeMb), nameof(CurrentSizeText), nameof(ApplyLabel),
        nameof(CanCancel), nameof(ShowLog), nameof(StageTitle)
    ];

    private readonly DiskImageService _images;
    private readonly ILogger<DiskEditViewModel> _logger;
    private DiskDevice _original = new();
    private QemuInstallation? _qemu;

    public DiskEditViewModel(DiskImageService images, ILogger<DiskEditViewModel> logger)
    {
        _images = images;
        _logger = logger;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || Array.IndexOf(DerivedProperties, e.PropertyName) >= 0)
                return;
            RaiseDerived();
        };
    }

    public DiskInterface[] Interfaces { get; } = Enum.GetValues<DiskInterface>();
    public DiskCacheMode[] CacheModes { get; } = Enum.GetValues<DiskCacheMode>();
    public string[] SizeUnits { get; } = ["MB", "GB", "TB"];
    public string[] RepairModes { get; } = [CheckOnly, RepairLeaks, RepairAll];

    public const string CheckOnly = "Check only";
    public const string RepairLeaks = "Check and repair leaked clusters";
    public const string RepairAll = "Check and repair everything";

    /// <summary>Formats that can be written, minus LUKS, which would need a passphrase file here.</summary>
    public IReadOnlyList<DiskFormatInfo> ConvertFormats { get; } =
        DiskFormatCatalog.Creatable.Where(f => f.Format != DiskFormat.Luks).ToList();

    public IReadOnlyList<DiskFormatInfo> BackingFormats { get; } =
        DiskFormatCatalog.All.Where(f => f.Format != DiskFormat.Luks).ToList();

    public ObservableCollection<ImageOptionViewModel> ConvertOptions { get; } = [];
    public ObservableCollection<ImageOptionViewModel> AmendOptions { get; } = [];

    /// <summary>The qcow2 preallocation choice for a resize; shown only when growing a qcow2 image.</summary>
    public ImageOptionViewModel PreallocationOption { get; } = new(
        new ImageOption("preallocation", "Preallocation", "How the added space is reserved",
            ImageOptionKind.Choice, Choices: DiskFormatCatalog.Qcow2PreallocationModes(OperatingSystem.IsWindows())));

    [ObservableProperty] private DiskEditStage _stage;

    // How the disk attaches
    [ObservableProperty] private DiskInterface _selectedInterface = DiskInterface.VirtIo;
    [ObservableProperty] private DiskCacheMode _selectedCache = DiskCacheMode.WriteBack;
    [ObservableProperty] private bool _isBoot;
    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _isCdRom;
    [ObservableProperty] private bool _isSnapshot;

    // What the image is like now
    [ObservableProperty] private string _imageInfo = string.Empty;
    [ObservableProperty] private string _backingFile = string.Empty;
    [ObservableProperty] private long _currentSizeMb;

    [ObservableProperty] private bool _checkEnabled;
    [ObservableProperty] private string _repairMode = CheckOnly;

    [ObservableProperty] private bool _amendEnabled;

    [ObservableProperty] private bool _rebaseEnabled;
    [ObservableProperty] private bool _rebaseFlatten;
    [ObservableProperty] private string _backingPath = string.Empty;
    [ObservableProperty] private DiskFormatInfo? _backingFormat;
    [ObservableProperty] private bool _rebaseUnsafe;

    [ObservableProperty] private bool _resizeEnabled;
    [ObservableProperty] private decimal _newSizeValue = 1;
    [ObservableProperty] private string _newSizeUnit = "GB";

    [ObservableProperty] private bool _convertEnabled;
    [ObservableProperty] private DiskFormatInfo? _convertFormat;
    [ObservableProperty] private string _convertPath = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _log = string.Empty;

    /// <summary>Set by the window: opens a file picker (save mode for a new file).</summary>
    public Func<bool, string, Task<string?>>? FilePicker { get; set; }

    public DiskDevice? Result { get; private set; }

    public event EventHandler? CloseRequested;

    public string FilePath => _original.FilePath;
    public DiskFormatInfo Format => DiskFormatCatalog.Get(_original.Format);

    public bool IsEditing => Stage == DiskEditStage.Editing;
    public bool IsConfirming => Stage == DiskEditStage.Confirming;
    public bool IsRunning => Stage == DiskEditStage.Running;
    public bool IsDone => Stage == DiskEditStage.Done;
    public bool CanEditSettings => IsEditing;
    public bool CanCancel => Stage is DiskEditStage.Editing or DiskEditStage.Confirming;
    public bool ShowLog => Stage is DiskEditStage.Running or DiskEditStage.Done;

    public string StageTitle => Stage switch
    {
        DiskEditStage.Editing => "Settings",
        DiskEditStage.Confirming => "Review before running",
        DiskEditStage.Running => "Running qemu-img",
        _ => "Finished"
    };

    /// <summary>A read-only format is always attached read-only, so the box is locked on.</summary>
    public bool CanEditReadOnly => IsEditing && !Format.IsReadOnly;

    public bool IsLocalFile
    {
        get
        {
            var kind = DiskConnection.FromJson(_original.ConnectionJson)?.Kind ?? DiskSourceKind.LocalFile;
            return kind == DiskSourceKind.LocalFile && !_original.FilePath.Contains("://", StringComparison.Ordinal);
        }
    }

    /// <summary>Why the image cannot be changed from here, or empty when it can.</summary>
    public string OperationsNote
    {
        get
        {
            if (!IsLocalFile)
                return "This disk is reached over the network. Its image is not changed from here; edit how it attaches below.";
            if (Format.Format == DiskFormat.Luks)
                return "Encrypted (LUKS) images are not changed from here yet; edit how it attaches below.";
            if (_qemu is null)
                return "There is no default QEMU installation, and it supplies the qemu-img that changes images. Set one on the Installations page.";
            if (!File.Exists(_original.FilePath))
                return "The image file does not exist, so there is nothing to change.";
            return string.Empty;
        }
    }

    public bool OperationsAvailable => OperationsNote.Length == 0;
    public bool HasOperationsNote => !OperationsAvailable;

    public bool CanCheck => OperationsAvailable && Format.CanCheck;
    public bool CanAmend => OperationsAvailable && Format.CanAmend;
    public bool CanRebase => OperationsAvailable && Format.CanRebase;
    public bool CanResize => OperationsAvailable && Format.CanResize;
    public bool CanConvert => OperationsAvailable;
    public bool ShowResizeNote => OperationsAvailable && !Format.CanResize;
    public string ResizeNote => $"{Format.Label} images cannot be resized by qemu-img. Convert to qcow2 or raw to change the size.";

    public bool IsShrinking => ResizeEnabled && CurrentSizeMb > 0 && NewSizeMb < CurrentSizeMb;
    public bool ShowPreallocation => ResizeEnabled && Format.Format == DiskFormat.QCow2 && !IsShrinking;

    public string CurrentSizeText => CurrentSizeMb > 0 ? $"Current size {FormatSize(CurrentSizeMb)}" : string.Empty;

    public long NewSizeMb
    {
        get
        {
            var factor = NewSizeUnit switch { "TB" => 1024L * 1024, "GB" => 1024L, _ => 1L };
            return (long)Math.Round(NewSizeValue * factor);
        }
    }

    public string ApplyLabel => HasPlan ? "Review changes..." : "Save";

    public string? ValidationMessage => Validate();
    public bool HasValidationMessage => ValidationMessage is not null;
    public bool HasPlan => OperationsAvailable && BuildPlan().Count > 0;

    public string CommandPreview => string.Join(Environment.NewLine, BuildPlan().Select(p => FormatCommand(p.Operation)));

    public string WarningText => string.Join(Environment.NewLine,
        BuildPlan().Select(p => p.Operation.Warning).Where(w => !string.IsNullOrEmpty(w)).Distinct());

    public bool HasWarnings => WarningText.Length > 0;

    /// <summary>Fills the dialog from a disk. Works on a copy: the caller's disk is untouched until it takes <see cref="Result"/>.</summary>
    public async Task InitializeAsync(DiskDevice disk, QemuInstallation? qemu)
    {
        _original = disk;
        _qemu = qemu;

        SelectedInterface = disk.Interface;
        SelectedCache = disk.CacheMode;
        IsBoot = disk.IsBootDevice;
        IsReadOnly = disk.IsReadOnly || Format.IsReadOnly;
        IsCdRom = disk.IsCdRom;
        IsSnapshot = disk.IsSnapshotMode;

        ConvertFormat = ConvertFormats.First(f => f.Format == (disk.Format == DiskFormat.QCow2 ? DiskFormat.Raw : DiskFormat.QCow2));
        BackingFormat = BackingFormats.First(f => f.Format == DiskFormat.Raw);

        AmendOptions.Clear();
        foreach (var definition in DiskFormatCatalog.Qcow2AmendOptions)
        {
            var option = new ImageOptionViewModel(definition, "(leave unchanged)");
            option.PropertyChanged += OnOptionChanged;
            AmendOptions.Add(option);
        }

        PreallocationOption.PropertyChanged += OnOptionChanged;

        OnPropertyChanged(string.Empty);
        await RefreshInfoAsync();
    }

    private async Task RefreshInfoAsync()
    {
        if (!OperationsAvailable)
        {
            ImageInfo = string.Empty;
            return;
        }

        ImageInfo = "Reading the image...";
        DiskImageInfo? info = null;
        try
        {
            info = await _images.GetImageInfoAsync(
                DiskImageService.ResolveQemuImg(_qemu!.BinaryDirectory), _original.FilePath, format: _original.Format);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}", _original.FilePath);
        }

        if (info is null)
        {
            ImageInfo = "qemu-img could not read this image. It may be in use by a running machine.";
            CurrentSizeMb = 0;
            RaiseDerived();
            return;
        }

        CurrentSizeMb = (long)Math.Ceiling(info.SizeBytes / 1048576d);
        BackingFile = info.BackingFile ?? string.Empty;
        ImageInfo = $"{Format.Label} · virtual size {FormatSize(CurrentSizeMb)} · {FormatBytes(info.ActualBytes)} on disk"
                    + (BackingFile.Length > 0 ? $" · backing file {BackingFile}" : string.Empty);

        if (CurrentSizeMb % 1024 == 0) { NewSizeUnit = "GB"; NewSizeValue = CurrentSizeMb / 1024; }
        else { NewSizeUnit = "MB"; NewSizeValue = CurrentSizeMb; }

        RaiseDerived();
    }

    partial void OnConvertFormatChanged(DiskFormatInfo? value)
    {
        foreach (var option in ConvertOptions)
            option.PropertyChanged -= OnOptionChanged;
        ConvertOptions.Clear();

        if (value is not null)
        {
            // A backing file belongs to "rebase"; offering it on convert only invites mistakes.
            foreach (var definition in value.CreateOptions.Where(o => o.Key is not ("backing_file" or "backing_fmt")))
            {
                var option = new ImageOptionViewModel(definition);
                option.PropertyChanged += OnOptionChanged;
                ConvertOptions.Add(option);
            }

            if (ConvertEnabled && !string.IsNullOrWhiteSpace(ConvertPath))
                ConvertPath = System.IO.Path.ChangeExtension(ConvertPath, value.QemuName);
        }
    }

    partial void OnConvertEnabledChanged(bool value)
    {
        if (value && string.IsNullOrWhiteSpace(ConvertPath) && ConvertFormat is { } format)
        {
            var directory = System.IO.Path.GetDirectoryName(_original.FilePath) ?? string.Empty;
            var name = System.IO.Path.GetFileNameWithoutExtension(_original.FilePath);
            ConvertPath = System.IO.Path.Combine(directory, $"{name}-converted.{format.QemuName}");
        }
    }

    partial void OnBackingPathChanged(string value)
    {
        // Guess the backing format from the extension; the user can still change it.
        var extension = System.IO.Path.GetExtension(value).TrimStart('.');
        if (DiskFormatCatalog.FromQemuName(extension) is { } guessed && guessed != DiskFormat.Luks)
            BackingFormat = BackingFormats.First(f => f.Format == guessed);
    }

    private void OnOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaiseDerived();

    private void RaiseDerived()
    {
        foreach (var name in DerivedProperties)
            OnPropertyChanged(name);
    }

    private string? Validate()
    {
        if (!OperationsAvailable)
            return null;

        if (ResizeEnabled)
        {
            if (CurrentSizeMb <= 0)
                return "The current size could not be read, so the image cannot be resized.";
            if (NewSizeMb < 1)
                return "The new size must be at least 1 MB.";
            if (NewSizeMb == CurrentSizeMb)
                return "The new size is the current size; there is nothing to resize.";
            if (NewSizeMb < CurrentSizeMb && !Format.CanShrink)
                return $"{Format.Label} images cannot be shrunk.";
        }

        if (RebaseEnabled && !RebaseFlatten)
        {
            if (string.IsNullOrWhiteSpace(BackingPath))
                return "Choose the new backing file.";
            if (!File.Exists(BackingPath.Trim()))
                return "The backing file does not exist.";
            if (BackingFormat is null)
                return "Choose the format of the backing file.";
        }

        if (AmendEnabled && !AmendOptions.Any(o => o.IsSet))
            return "Choose at least one setting to change.";

        if (ConvertEnabled)
        {
            if (ConvertFormat is null)
                return "Choose the format to convert to.";
            if (string.IsNullOrWhiteSpace(ConvertPath))
                return "Choose where to write the converted image.";
            if (!FolderExists(ConvertPath))
                return "The folder for the converted image does not exist.";
            if (SamePath(ConvertPath, _original.FilePath))
                return "The converted image needs a different file name from the original.";
            if (File.Exists(ConvertPath.Trim()))
                return "A file with that name already exists. It will not be overwritten.";
        }

        return null;
    }

    private List<Planned> BuildPlan()
    {
        var plan = new List<Planned>();
        if (!OperationsAvailable)
            return plan;

        var path = _original.FilePath;
        var format = _original.Format;

        if (CheckEnabled && Format.CanCheck)
        {
            var repair = RepairMode switch { RepairLeaks => "leaks", RepairAll => "all", _ => null };
            plan.Add(new(OperationKind.Check, new DiskImageOperation(
                repair is null ? "Check the image" : "Check and repair the image",
                DiskImageService.BuildCheckArguments(path, format, repair),
                repair is null ? null : "Repair rewrites the image's metadata. Make a copy of the file first if the contents matter.")));
        }

        if (AmendEnabled && Format.CanAmend && AmendOptions.Any(o => o.IsSet))
        {
            var options = AmendOptions.Where(o => o.IsSet).ToDictionary(o => o.Definition.Key, o => o.Value);
            plan.Add(new(OperationKind.Amend, new DiskImageOperation(
                "Change image settings",
                DiskImageService.BuildAmendArguments(path, format, options),
                options.TryGetValue("compat", out var compat) && compat == "0.10"
                    ? "Compatibility 0.10 downgrades the image; features that need v3 are removed."
                    : null)));
        }

        if (RebaseEnabled && Format.CanRebase && (RebaseFlatten || (!string.IsNullOrWhiteSpace(BackingPath) && BackingFormat is not null)))
        {
            var backing = RebaseFlatten ? null : BackingPath.Trim();
            var unsafeMode = RebaseUnsafe && !RebaseFlatten;
            plan.Add(new(OperationKind.Rebase, new DiskImageOperation(
                RebaseFlatten ? "Merge the backing file into the image" : "Change the backing file",
                DiskImageService.BuildRebaseArguments(path, format, backing, BackingFormat?.Format, unsafeMode),
                unsafeMode ? "Unsafe rebase only changes the pointer. If the new backing file does not hold the same data as the old one, the image becomes corrupt." : null)));
        }

        if (ResizeEnabled && Format.CanResize && NewSizeMb >= 1 && CurrentSizeMb > 0 && NewSizeMb != CurrentSizeMb)
        {
            var shrink = NewSizeMb < CurrentSizeMb;
            var prealloc = ShowPreallocation && PreallocationOption.IsSet ? PreallocationOption.Value : null;
            plan.Add(new(OperationKind.Resize, new DiskImageOperation(
                shrink ? $"Shrink to {FormatSize(NewSizeMb)}" : $"Grow to {FormatSize(NewSizeMb)}",
                DiskImageService.BuildResizeArguments(path, format, NewSizeMb, shrink, prealloc),
                shrink
                    ? "Shrinking discards everything past the new end. Shrink the partitions and file systems inside the guest first, or data is lost."
                    : "The guest still has to grow its partition and file system to use the new space.")));
        }

        if (ConvertEnabled && ConvertFormat is { } target && !string.IsNullOrWhiteSpace(ConvertPath))
        {
            var destination = ConvertPath.Trim();
            var options = ConvertOptions.Where(o => o.IsSet).ToDictionary(o => o.Definition.Key, o => o.Value);
            plan.Add(new(OperationKind.Convert, new DiskImageOperation(
                $"Convert to {target.Label}",
                DiskImageService.BuildConvertArguments(path, format, destination, target.Format, options),
                "Conversion writes a new file and keeps the original. This disk will use the new file afterwards.",
                destination)));
        }

        return plan;
    }

    private static string FormatCommand(DiskImageOperation operation)
        => "qemu-img " + string.Join(' ', operation.Arguments.Select(a => a.Length == 0 || a.Contains(' ') ? $"\"{a}\"" : a));

    private static string FormatSize(long mb)
        => mb % (1024 * 1024) == 0 ? $"{mb / (1024 * 1024)} TB"
         : mb % 1024 == 0 ? $"{mb / 1024} GB"
         : $"{mb} MB";

    private static string FormatBytes(long bytes)
        => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB"
         : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.##} MB"
         : $"{bytes / 1024d:0.##} KB";

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

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                System.IO.Path.GetFullPath(a.Trim()), System.IO.Path.GetFullPath(b.Trim()),
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The disk as it should be after the dialog: the attach settings, and the new file if it was converted.</summary>
    private DiskDevice BuildDevice(string? convertedPath = null, DiskFormat? convertedFormat = null)
    {
        var format = convertedFormat ?? _original.Format;
        return new DiskDevice
        {
            Id = _original.Id,
            VmId = _original.VmId,
            Index = _original.Index,
            FilePath = convertedPath ?? _original.FilePath,
            Format = format,
            Interface = SelectedInterface,
            CacheMode = SelectedCache,
            IsBootDevice = IsBoot,
            IsReadOnly = IsReadOnly || DiskFormatCatalog.IsReadOnly(format),
            IsCdRom = IsCdRom,
            IsSnapshotMode = IsSnapshot,
            ConnectionJson = _original.ConnectionJson
        };
    }

    [RelayCommand]
    private void Apply()
    {
        if (Validate() is { } message)
        {
            StatusMessage = message;
            return;
        }

        StatusMessage = string.Empty;
        if (BuildPlan().Count == 0)
        {
            Result = BuildDevice();
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        Stage = DiskEditStage.Confirming;
    }

    [RelayCommand]
    private void Back()
    {
        StatusMessage = string.Empty;
        Stage = DiskEditStage.Editing;
    }

    [RelayCommand]
    private void Cancel()
    {
        // A running qemu-img is not interrupted: killing it halfway through a resize can damage the image.
        if (IsRunning) return;
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        var plan = BuildPlan();
        if (plan.Count == 0 || _qemu is null)
            return;

        var qemuImg = DiskImageService.ResolveQemuImg(_qemu.BinaryDirectory);
        Stage = DiskEditStage.Running;
        StatusMessage = string.Empty;
        Log = string.Empty;

        string? convertedPath = null;
        DiskFormat? convertedFormat = null;
        var applied = new List<OperationKind>();

        try
        {
            foreach (var step in plan)
            {
                AppendLog($"> {FormatCommand(step.Operation)}");
                var output = await _images.RunAsync(qemuImg, step.Operation);
                if (output.Length > 0) AppendLog(output);
                applied.Add(step.Kind);
                _logger.LogInformation("qemu-img {Verb} finished for {Path}", step.Operation.Arguments[0], _original.FilePath);

                if (step.Kind == OperationKind.Convert)
                {
                    convertedPath = step.Operation.OutputPath;
                    convertedFormat = ConvertFormat!.Format;
                }
            }

            AppendLog("Done.");
            Result = BuildDevice(convertedPath, convertedFormat);
            Stage = DiskEditStage.Done;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Changing {Path} failed", _original.FilePath);
            AppendLog(ex.Message);
            StatusMessage = applied.Count > 0
                ? $"{ex.Message}  The steps before it were already applied and are no longer selected."
                : ex.Message;

            // Steps that did run must not run again if the user retries.
            foreach (var kind in applied)
                DisableOperation(kind);

            Stage = DiskEditStage.Editing;
            await RefreshInfoAsync();
        }

        RaiseDerived();
    }

    private void DisableOperation(OperationKind kind)
    {
        switch (kind)
        {
            case OperationKind.Check: CheckEnabled = false; break;
            case OperationKind.Amend: AmendEnabled = false; break;
            case OperationKind.Rebase: RebaseEnabled = false; break;
            case OperationKind.Resize: ResizeEnabled = false; break;
            case OperationKind.Convert: ConvertEnabled = false; break;
        }
    }

    private void AppendLog(string text) => Log = Log.Length == 0 ? text : Log + Environment.NewLine + text;

    [RelayCommand]
    private async Task BrowseConvertPathAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(true, "Write the converted image as");
        if (!string.IsNullOrEmpty(picked)) ConvertPath = picked;
    }

    [RelayCommand]
    private async Task BrowseBackingAsync()
    {
        if (FilePicker is null) return;
        var picked = await FilePicker(false, "Choose the backing file");
        if (!string.IsNullOrEmpty(picked)) BackingPath = picked;
    }
}
