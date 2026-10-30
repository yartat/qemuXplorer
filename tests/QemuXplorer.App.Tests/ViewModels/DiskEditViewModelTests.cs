using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.App.ViewModels;
using QemuXplorer.App.Views;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

public class DiskEditViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-diskedit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    private static DiskEditViewModel CreateSut() =>
        new(new DiskImageService(NullLogger<DiskImageService>.Instance), NullLogger<DiskEditViewModel>.Instance);

    private static QemuInstallation Qemu(string? directory = null) =>
        new() { BinaryDirectory = directory ?? Path.GetTempPath() };

    private static string? RealQemuImgDirectory()
    {
        var exe = OperatingSystem.IsWindows() ? "qemu-img.exe" : "qemu-img";
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat([@"C:\Program Files\qemu", "/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"]);
        return candidates.FirstOrDefault(d =>
        {
            try { return File.Exists(Path.Combine(d, exe)); }
            catch (ArgumentException) { return false; }
        });
    }

    /// <summary>A disk whose file exists (empty), so the dialog treats it as a local image.</summary>
    private DiskDevice LocalDisk(DiskFormat format, string name = "disk.img")
    {
        var path = P(name);
        File.WriteAllText(path, "x");
        return new DiskDevice
        {
            FilePath = path,
            Format = format,
            Interface = DiskInterface.VirtIo,
            Index = 3,
            VmId = Guid.NewGuid()
        };
    }

    private async Task<DiskEditViewModel> ReadyAsync(DiskDevice disk, QemuInstallation? qemu = null, long sizeMb = 20480)
    {
        var sut = CreateSut();
        await sut.InitializeAsync(disk, qemu ?? Qemu());
        sut.CurrentSizeMb = sizeMb;
        return sut;
    }

    // ---- which operations a disk offers ----

    [AvaloniaFact]
    public async Task Qcow2_OffersEveryOperation()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2));

        (sut.CanResize, sut.CanConvert, sut.CanRebase, sut.CanAmend, sut.CanCheck)
            .Should().Be((true, true, true, true, true));
        sut.HasOperationsNote.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Raw_CanResizeAndConvert_ButNotCheckAmendOrRebase()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Raw));

        (sut.CanResize, sut.CanConvert, sut.CanRebase, sut.CanAmend, sut.CanCheck)
            .Should().Be((true, true, false, false, false));
    }

    [AvaloniaFact]
    public async Task Vdi_CannotBeResized_AndSaysToConvertInstead()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Vdi));

        sut.CanResize.Should().BeFalse();
        sut.ShowResizeNote.Should().BeTrue();
        sut.ResizeNote.Should().Contain("Convert");
        sut.CanCheck.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ADiskOverTheNetwork_OffersNoImageOperations()
    {
        var disk = new DiskDevice
        {
            FilePath = "nbd://host:10809/export",
            Format = DiskFormat.Raw,
            ConnectionJson = new DiskConnection { Kind = DiskSourceKind.Nbd, Host = "host", Port = 10809 }.ToJson()
        };

        var sut = await ReadyAsync(disk);

        sut.OperationsAvailable.Should().BeFalse();
        sut.OperationsNote.Should().Contain("network");
        (sut.CanResize || sut.CanConvert || sut.CanCheck).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AnEncryptedImage_IsLeftAlone()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Luks));

        sut.OperationsAvailable.Should().BeFalse();
        sut.OperationsNote.Should().Contain("LUKS");
    }

    [AvaloniaFact]
    public async Task WithoutADefaultQemu_NothingCanChangeTheImage()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(LocalDisk(DiskFormat.QCow2), qemu: null);

        sut.OperationsAvailable.Should().BeFalse();
        sut.OperationsNote.Should().Contain("Installations");
    }

    [AvaloniaFact]
    public async Task AMissingFile_OffersNoOperations()
    {
        var disk = LocalDisk(DiskFormat.QCow2);
        File.Delete(disk.FilePath);

        var sut = await ReadyAsync(disk);

        sut.OperationsAvailable.Should().BeFalse();
        sut.OperationsNote.Should().Contain("does not exist");
    }

    [AvaloniaFact]
    public async Task AReadOnlyFormat_StaysReadOnly_AndCanOnlyBeConverted()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Dmg));

        sut.IsReadOnly.Should().BeTrue();
        sut.CanEditReadOnly.Should().BeFalse();
        (sut.CanResize || sut.CanRebase || sut.CanAmend || sut.CanCheck).Should().BeFalse();
        sut.CanConvert.Should().BeTrue();
    }

    // ---- saving without touching the image ----

    [AvaloniaFact]
    public async Task SavingOnlyAttachSettings_ClosesAtOnceAndKeepsTheDisksIdentity()
    {
        var disk = LocalDisk(DiskFormat.QCow2);
        var sut = await ReadyAsync(disk);
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;
        sut.SelectedInterface = DiskInterface.Scsi;
        sut.IsBoot = true;

        sut.ApplyCommand.Execute(null);

        closed.Should().BeTrue();
        sut.Stage.Should().Be(DiskEditStage.Editing);
        var result = sut.Result!;
        (result.Id, result.VmId, result.Index, result.FilePath).Should().Be((disk.Id, disk.VmId, 3, disk.FilePath));
        (result.Interface, result.IsBootDevice, result.Format).Should().Be((DiskInterface.Scsi, true, DiskFormat.QCow2));
    }

    [AvaloniaFact]
    public async Task Cancel_ReturnsNoDisk()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2));
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;
        sut.SelectedInterface = DiskInterface.Ide;

        sut.CancelCommand.Execute(null);

        closed.Should().BeTrue();
        sut.Result.Should().BeNull();
    }

    // ---- planning ----

    [AvaloniaFact]
    public async Task ShrinkingQcow2_NeedsTheShrinkFlag_AndWarnsAboutDataLoss()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.NewSizeUnit = "GB";
        sut.NewSizeValue = 10;

        sut.IsShrinking.Should().BeTrue();
        sut.CommandPreview.Should().Contain("resize -f qcow2 --shrink").And.Contain("10240M");
        sut.WarningText.Should().Contain("discards");
        sut.ShowPreallocation.Should().BeFalse("preallocation only means something when growing");
    }

    [AvaloniaFact]
    public async Task GrowingQcow2_OffersPreallocation_AndPassesTheChosenMode()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 40;
        sut.PreallocationOption.SelectedChoice = "metadata";

        sut.ShowPreallocation.Should().BeTrue();
        sut.CommandPreview.Should().Contain("--preallocation=metadata").And.NotContain("--shrink");
    }

    [AvaloniaFact]
    public async Task ShrinkingQed_IsRefusedBeforeAnythingRuns()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Qed), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 10;

        sut.ValidationMessage.Should().Contain("cannot be shrunk");
        sut.ApplyCommand.Execute(null);
        sut.Stage.Should().Be(DiskEditStage.Editing);
        sut.StatusMessage.Should().Contain("cannot be shrunk");
    }

    [AvaloniaFact]
    public async Task ResizingToTheCurrentSize_IsRejected()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Raw), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 20;

        sut.ValidationMessage.Should().Contain("nothing to resize");
    }

    [AvaloniaFact]
    public async Task SelectedOperations_RunInAFixedOrder_ConvertLast()
    {
        var disk = LocalDisk(DiskFormat.QCow2);
        var backing = P("base.raw");
        File.WriteAllText(backing, "x");
        var sut = await ReadyAsync(disk, sizeMb: 20480);
        sut.ConvertEnabled = true;
        sut.ConvertPath = P("out.raw");
        sut.ConvertFormat = sut.ConvertFormats.First(f => f.Format == DiskFormat.Raw);
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 40;
        sut.RebaseEnabled = true;
        sut.BackingPath = backing;
        sut.AmendEnabled = true;
        sut.AmendOptions.First(o => o.Definition.Key == "compat").SelectedChoice = "1.1";
        sut.CheckEnabled = true;

        var verbs = sut.CommandPreview.Split(Environment.NewLine).Select(l => l.Split(' ')[1]).ToArray();

        verbs.Should().Equal("check", "amend", "rebase", "resize", "convert");
    }

    [AvaloniaFact]
    public async Task Amend_WithNothingChosen_IsRejected_AndLazyRefcountsCanBeSwitchedOff()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2));
        sut.AmendEnabled = true;

        sut.ValidationMessage.Should().Contain("at least one setting");

        sut.AmendOptions.First(o => o.Definition.Key == "lazy_refcounts").SelectedChoice = "off";

        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("lazy_refcounts=off");
    }

    [AvaloniaFact]
    public async Task Rebase_ToANewFile_NeedsAnExistingBackingFile_AndGuessesItsFormat()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2));
        sut.RebaseEnabled = true;

        sut.ValidationMessage.Should().Contain("backing file");

        var backing = P("base.qcow2");
        File.WriteAllText(backing, "x");
        sut.BackingPath = backing;

        sut.ValidationMessage.Should().BeNull();
        sut.BackingFormat!.Format.Should().Be(DiskFormat.QCow2);
        sut.CommandPreview.Should().Contain("-F qcow2");
    }

    [AvaloniaFact]
    public async Task Rebase_Flatten_NeedsNoBackingFile_AndIgnoresUnsafe()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2));
        sut.RebaseEnabled = true;
        sut.RebaseFlatten = true;
        sut.RebaseUnsafe = true;

        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().Contain("rebase -f qcow2 -b \"\"").And.NotContain(" -u ");
    }

    [AvaloniaFact]
    public async Task Convert_NeedsANewFileInAnExistingFolder()
    {
        var disk = LocalDisk(DiskFormat.Raw);
        var sut = await ReadyAsync(disk);
        sut.ConvertEnabled = true;

        sut.ConvertPath = disk.FilePath;
        sut.ValidationMessage.Should().Contain("different file name");

        var existing = P("taken.qcow2");
        File.WriteAllText(existing, "x");
        sut.ConvertPath = existing;
        sut.ValidationMessage.Should().Contain("already exists");

        sut.ConvertPath = Path.Combine(_dir, "no-such-folder", "x.qcow2");
        sut.ValidationMessage.Should().Contain("folder");

        sut.ConvertPath = P("fresh.qcow2");
        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task EnablingConvert_SuggestsAFileNextToTheOriginalInTheTargetFormat()
    {
        var disk = LocalDisk(DiskFormat.Raw, "data.img");
        var sut = await ReadyAsync(disk);

        sut.ConvertEnabled = true;

        sut.ConvertFormat!.Format.Should().Be(DiskFormat.QCow2);
        sut.ConvertPath.Should().Be(P("data-converted.qcow2"));

        sut.ConvertFormat = sut.ConvertFormats.First(f => f.Format == DiskFormat.Vmdk);

        sut.ConvertPath.Should().Be(P("data-converted.vmdk"));
    }

    [AvaloniaFact]
    public async Task ConvertOptions_FollowTheTargetFormat_WithoutABackingFileOption()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Raw));

        sut.ConvertFormat = sut.ConvertFormats.First(f => f.Format == DiskFormat.QCow2);

        sut.ConvertOptions.Select(o => o.Definition.Key).Should().Contain("cluster_size")
            .And.NotContain("backing_file").And.NotContain("backing_fmt");
    }

    // ---- confirming ----

    [AvaloniaFact]
    public async Task ReviewShowsTheCommandsFirst_AndBackReturnsToEditing()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Raw), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 40;

        sut.ApplyCommand.Execute(null);

        sut.Stage.Should().Be(DiskEditStage.Confirming);
        sut.IsConfirming.Should().BeTrue();
        sut.Result.Should().BeNull("nothing has run yet");

        sut.BackCommand.Execute(null);

        sut.Stage.Should().Be(DiskEditStage.Editing);
    }

    [AvaloniaFact]
    public async Task ApplyLabel_SaysReviewOnlyWhenTheImageWillChange()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.Raw), sizeMb: 20480);
        sut.ApplyLabel.Should().Be("Save");

        sut.ResizeEnabled = true;
        sut.NewSizeValue = 40;

        sut.ApplyLabel.Should().Contain("Review");
    }

    // ---- running, against the real qemu-img ----

    private async Task<(DiskEditViewModel Sut, DiskDevice Disk, string Qemu)> RealImageAsync(DiskFormat format, string name)
    {
        var directory = RealQemuImgDirectory();
        Assert.SkipUnless(directory is not null, "qemu-img is not installed on this machine");

        var qemuImg = DiskImageService.ResolveQemuImg(directory!);
        var path = P(name);
        var service = new DiskImageService(NullLogger<DiskImageService>.Instance);
        await service.CreateImageAsync(qemuImg, new DiskCreateRequest(path, format, 64));

        var disk = new DiskDevice { FilePath = path, Format = format, Index = 1, VmId = Guid.NewGuid() };
        var sut = CreateSut();
        await sut.InitializeAsync(disk, Qemu(directory));
        return (sut, disk, qemuImg);
    }

    [AvaloniaFact]
    public async Task Real_ReadsTheCurrentSizeFromTheImage()
    {
        var (sut, _, _) = await RealImageAsync(DiskFormat.QCow2, "a.qcow2");

        sut.CurrentSizeMb.Should().Be(64);
        sut.ImageInfo.Should().Contain("virtual size 64 MB");
        sut.NewSizeValue.Should().Be(64);
        sut.NewSizeUnit.Should().Be("MB");
    }

    [AvaloniaFact]
    public async Task Real_Resize_ChangesTheImage_AndTheDiskKeepsItsFile()
    {
        var (sut, disk, qemuImg) = await RealImageAsync(DiskFormat.QCow2, "a.qcow2");
        sut.ResizeEnabled = true;
        sut.NewSizeValue = 128;
        sut.ApplyCommand.Execute(null);

        await sut.RunCommand.ExecuteAsync(null);

        sut.Stage.Should().Be(DiskEditStage.Done);
        sut.Log.Should().Contain("Image resized");
        sut.Result!.FilePath.Should().Be(disk.FilePath);
        var info = await new DiskImageService(NullLogger<DiskImageService>.Instance)
            .GetImageInfoAsync(qemuImg, disk.FilePath, format: DiskFormat.QCow2);
        info!.SizeBytes.Should().Be(128L << 20);
    }

    [AvaloniaFact]
    public async Task Real_Convert_MakesADiskThatPointsAtTheNewFile()
    {
        var (sut, disk, _) = await RealImageAsync(DiskFormat.Raw, "a.raw");
        var target = P("b.qcow2");
        sut.ConvertEnabled = true;
        sut.ConvertFormat = sut.ConvertFormats.First(f => f.Format == DiskFormat.QCow2);
        sut.ConvertPath = target;
        sut.ApplyCommand.Execute(null);

        await sut.RunCommand.ExecuteAsync(null);

        sut.Stage.Should().Be(DiskEditStage.Done);
        File.Exists(disk.FilePath).Should().BeTrue("the original is kept");
        File.Exists(target).Should().BeTrue();
        (sut.Result!.FilePath, sut.Result.Format, sut.Result.Id).Should().Be((target, DiskFormat.QCow2, disk.Id));
    }

    [AvaloniaFact]
    public async Task Real_AFailingStep_StaysInTheDialog_AndDoesNotRepeatTheStepsThatWorked()
    {
        var (sut, disk, _) = await RealImageAsync(DiskFormat.QCow2, "a.qcow2");
        var backing = P("base.raw");
        File.WriteAllText(backing, "x");
        sut.AmendEnabled = true;
        sut.AmendOptions.First(o => o.Definition.Key == "lazy_refcounts").SelectedChoice = "on";
        sut.RebaseEnabled = true;
        sut.BackingPath = backing;
        sut.ApplyCommand.Execute(null);
        File.Delete(backing);

        await sut.RunCommand.ExecuteAsync(null);

        sut.Stage.Should().Be(DiskEditStage.Editing);
        sut.Result.Should().BeNull();
        sut.StatusMessage.Should().Contain("already applied");
        sut.AmendEnabled.Should().BeFalse("amend ran before rebase failed");
        sut.RebaseEnabled.Should().BeTrue();
        File.Exists(disk.FilePath).Should().BeTrue();
    }

    // ---- the window itself ----

    [AvaloniaFact]
    public async Task TheWindow_BindsToTheViewModelInEveryStage()
    {
        var sut = await ReadyAsync(LocalDisk(DiskFormat.QCow2), sizeMb: 20480);
        sut.ResizeEnabled = true;
        sut.ConvertEnabled = true;
        sut.RebaseEnabled = true;
        sut.AmendEnabled = true;
        sut.CheckEnabled = true;
        sut.NewSizeValue = 40;

        var window = new DiskEditView { DataContext = sut };
        window.Show();
        sut.ApplyCommand.Execute(null);
        sut.BackCommand.Execute(null);

        window.IsVisible.Should().BeTrue();
        window.Close();
    }
}
