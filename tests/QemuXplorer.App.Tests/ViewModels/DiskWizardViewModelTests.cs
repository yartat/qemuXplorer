using Avalonia.Headless.XUnit;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.App.ViewModels;
using QemuXplorer.Core.Services;
using QemuXplorer.Models;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests.ViewModels;

public class DiskWizardViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-wizard-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DiskWizardViewModel CreateSut() =>
        new(new DiskImageService(NullLogger<DiskImageService>.Instance));

    private static QemuInstallation Qemu() => new() { BinaryDirectory = Path.GetTempPath() };

    private static SourceChoice Source(DiskSourceMode mode) => DiskWizardViewModel.Sources.First(s => s.Mode == mode);

    private async Task<DiskWizardViewModel> ReadyAsync(DiskSourceMode mode, bool firstDisk = true)
    {
        var sut = CreateSut();
        await sut.InitializeAsync(Qemu(), firstDisk);
        sut.SelectedSource = Source(mode);
        return sut;
    }

    [AvaloniaFact]
    public void StartsOnTheSourceStep_OfferingCreateNew()
    {
        var sut = CreateSut();

        sut.Step.Should().Be(0);
        sut.IsStep0.Should().BeTrue();
        sut.Mode.Should().Be(DiskSourceMode.CreateNew);
    }

    [AvaloniaFact]
    public void OffersAllFiveSources() =>
        DiskWizardViewModel.Sources.Select(s => s.Mode).Should().BeEquivalentTo(Enum.GetValues<DiskSourceMode>());

    [AvaloniaFact]
    public void CreateNew_OffersOnlyFormatsQemuImgCanCreate()
    {
        var sut = CreateSut();

        sut.AvailableFormats.Should().OnlyContain(f => f.CanCreate);
        sut.AvailableFormats.Select(f => f.Format).Should().NotContain([DiskFormat.Bochs, DiskFormat.Cloop, DiskFormat.Dmg, DiskFormat.Parallels]);
    }

    [AvaloniaFact]
    public async Task ExistingFile_OffersEveryFormatIncludingReadOnlyOnes()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);

        sut.AvailableFormats.Select(f => f.Format).Should().BeEquivalentTo(Enum.GetValues<DiskFormat>());
    }

    [AvaloniaTheory]
    [InlineData(DiskSourceMode.Nbd, 10809)]
    [InlineData(DiskSourceMode.Ssh, 22)]
    [InlineData(DiskSourceMode.Iscsi, 3260)]
    public async Task NetworkSource_SelectsItsDefaultPortAndRawFormat(DiskSourceMode mode, int port)
    {
        var sut = await ReadyAsync(mode);

        sut.Port.Should().Be(port);
        sut.SelectedFormat!.Format.Should().Be(DiskFormat.Raw);
        sut.IsRemote.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ChoosingAFormat_LoadsItsCreationOptions()
    {
        var sut = CreateSut();

        sut.SelectedFormat = sut.AvailableFormats.First(f => f.Format == DiskFormat.QCow2);

        sut.Options.Select(o => o.Definition.Key).Should().Contain(["preallocation", "cluster_size", "lazy_refcounts"]);
        sut.ShowOptions.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Raw_HasNoCreationOptions()
    {
        var sut = CreateSut();

        sut.SelectedFormat = sut.AvailableFormats.First(f => f.Format == DiskFormat.Raw);

        sut.Options.Should().BeEmpty();
        sut.ShowOptions.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task CreateNew_WithoutADefaultQemu_ExplainsWhyItCannotContinue()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(null, true);
        sut.Path = Path.Combine(_dir, "a.qcow2");

        sut.ValidationMessage.Should().Contain("QEMU installation");
        sut.CanGoNext.Should().BeTrue("only step 1 gates on validation");
    }

    [AvaloniaFact]
    public async Task CreateNew_RefusesAFileThatAlreadyExists()
    {
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);
        var existing = Path.Combine(_dir, "taken.qcow2");
        File.WriteAllText(existing, "x");

        sut.Path = existing;

        sut.ValidationMessage.Should().Contain("already exists");
    }

    [AvaloniaFact]
    public async Task CreateNew_RefusesAMissingFolder()
    {
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);

        sut.Path = Path.Combine(_dir, "nope", "a.qcow2");

        sut.ValidationMessage.Should().Contain("folder");
    }

    [AvaloniaFact]
    public async Task CreateNew_WithAValidPath_PreviewsTheQemuImgCommand()
    {
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);
        sut.Path = Path.Combine(_dir, "a.qcow2");
        sut.SizeValue = 5;
        sut.SizeUnit = "GB";
        sut.Options.First(o => o.Definition.Key == "preallocation").SelectedChoice = "metadata";

        sut.ValidationMessage.Should().BeNull();
        sut.CommandPreview.Should().StartWith("qemu-img create -f qcow2 -o preallocation=metadata ");
        sut.CommandPreview.Should().EndWith("5120M");
    }

    [AvaloniaFact]
    public async Task OptionsLeftAtTheirDefault_AreNotPassedToQemuImg()
    {
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);
        sut.Path = Path.Combine(_dir, "a.qcow2");

        // Compare whole arguments: the path holds a random temp-folder name, and a substring check for
        // "-o" matched it ("qx-wizard-o5kz") about one run in thirty.
        sut.CommandPreview.Split(' ').Should().NotContain("-o");
    }

    [AvaloniaTheory]
    [InlineData("MB", 1)]
    [InlineData("GB", 1024)]
    [InlineData("TB", 1048576)]
    public void SizeUnit_ConvertsToMegabytes(string unit, long perUnit)
    {
        var sut = CreateSut();
        sut.SizeValue = 2;
        sut.SizeUnit = unit;

        sut.SizeMb.Should().Be(2 * perUnit);
    }

    [AvaloniaFact]
    public async Task Luks_DemandsAPassphraseFile()
    {
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);
        sut.Path = Path.Combine(_dir, "e.luks");
        sut.SelectedFormat = sut.AvailableFormats.First(f => f.Format == DiskFormat.Luks);

        sut.NeedsKey.Should().BeTrue();
        sut.ValidationMessage.Should().Contain("passphrase");

        var key = Path.Combine(_dir, "pass.txt");
        File.WriteAllText(key, "secret");
        sut.KeySecretFile = key;

        sut.ValidationMessage.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task ExistingFile_RequiresTheFileToExist()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);
        sut.Path = Path.Combine(_dir, "missing.img");

        sut.ValidationMessage.Should().Contain("does not exist");
    }

    [AvaloniaTheory]
    [InlineData(DiskFormat.Bochs)]
    [InlineData(DiskFormat.Cloop)]
    [InlineData(DiskFormat.Dmg)]
    [InlineData(DiskFormat.Parallels)]
    public async Task ReadOnlyFormat_IsAttachedReadOnlyAndLocksTheCheckbox(DiskFormat format)
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);
        var file = Path.Combine(_dir, "disk.img");
        File.WriteAllText(file, "x");
        sut.Path = file;
        sut.SelectedFormat = sut.AvailableFormats.First(f => f.Format == format);
        sut.IsReadOnly = false;

        sut.CanEditReadOnly.Should().BeFalse();
        sut.FormatIsReadOnly.Should().BeTrue();

        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);
        await sut.FinishCommand.ExecuteAsync(null);

        sut.Result!.IsReadOnly.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(DiskSourceMode.Nbd)]
    [InlineData(DiskSourceMode.Ssh)]
    [InlineData(DiskSourceMode.Iscsi)]
    public async Task NetworkSource_NeedsAHost(DiskSourceMode mode)
    {
        var sut = await ReadyAsync(mode);

        sut.ValidationMessage.Should().Contain("host");
    }

    [AvaloniaFact]
    public async Task Ssh_NeedsARemotePath()
    {
        var sut = await ReadyAsync(DiskSourceMode.Ssh);
        sut.Host = "box";

        sut.ValidationMessage.Should().Contain("path");
    }

    [AvaloniaFact]
    public async Task Iscsi_NeedsATarget()
    {
        var sut = await ReadyAsync(DiskSourceMode.Iscsi);
        sut.Host = "san";

        sut.ValidationMessage.Should().Contain("target");
    }

    [AvaloniaFact]
    public async Task NetworkSource_RejectsAnOutOfRangePort()
    {
        var sut = await ReadyAsync(DiskSourceMode.Nbd);
        sut.Host = "srv";
        sut.Port = 70000;

        sut.ValidationMessage.Should().Contain("port");
    }

    [AvaloniaFact]
    public async Task Next_OnTheDetailsStep_StaysPutWhileInvalid()
    {
        var sut = await ReadyAsync(DiskSourceMode.Nbd);
        sut.NextCommand.Execute(null);
        sut.Step.Should().Be(1);

        sut.NextCommand.Execute(null);

        sut.Step.Should().Be(1);
        sut.StatusMessage.Should().Contain("host");
    }

    [AvaloniaFact]
    public async Task Back_ReturnsToThePreviousStep()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);
        sut.NextCommand.Execute(null);

        sut.BackCommand.Execute(null);

        sut.Step.Should().Be(0);
        sut.CanGoBack.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Nbd_FinishProducesAUriAndStructuredConnection()
    {
        var sut = await ReadyAsync(DiskSourceMode.Nbd);
        sut.Host = "srv";
        sut.Export = "disk1";
        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);

        await sut.FinishCommand.ExecuteAsync(null);

        var disk = sut.Result!;
        disk.FilePath.Should().Be("nbd://srv:10809/disk1");
        disk.Format.Should().Be(DiskFormat.Raw);
        var connection = DiskConnection.FromJson(disk.ConnectionJson)!;
        connection.Kind.Should().Be(DiskSourceKind.Nbd);
        connection.Host.Should().Be("srv");
        connection.Export.Should().Be("disk1");
    }

    [AvaloniaFact]
    public async Task Ssh_FinishBuildsTheUriFromUserHostAndPath()
    {
        var sut = await ReadyAsync(DiskSourceMode.Ssh);
        sut.Host = "box";
        sut.User = "bob";
        sut.RemotePath = "/srv/d.img";
        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);

        await sut.FinishCommand.ExecuteAsync(null);

        sut.Result!.FilePath.Should().Be("ssh://bob@box:22/srv/d.img");
    }

    [AvaloniaFact]
    public async Task Iscsi_FinishBuildsTheUriFromTargetAndLun()
    {
        var sut = await ReadyAsync(DiskSourceMode.Iscsi);
        sut.Host = "san";
        sut.Target = "iqn.2001-04.com.example:d1";
        sut.Lun = 2;
        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);

        await sut.FinishCommand.ExecuteAsync(null);

        sut.Result!.FilePath.Should().Be("iscsi://san:3260/iqn.2001-04.com.example%3Ad1/2");
    }

    [AvaloniaFact]
    public async Task ExistingFile_FinishCarriesTheAttachSettings()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile, firstDisk: false);
        var file = Path.Combine(_dir, "d.qcow2");
        File.WriteAllText(file, "x");
        sut.Path = file;
        sut.SelectedInterface = DiskInterface.Nvme;
        sut.SelectedCache = DiskCacheMode.None;
        sut.IsSnapshot = true;
        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);

        await sut.FinishCommand.ExecuteAsync(null);

        var disk = sut.Result!;
        disk.FilePath.Should().Be(file);
        disk.Interface.Should().Be(DiskInterface.Nvme);
        disk.CacheMode.Should().Be(DiskCacheMode.None);
        disk.IsSnapshotMode.Should().BeTrue();
        disk.IsBootDevice.Should().BeFalse();
        disk.ConnectionJson.Should().BeEmpty("a plain local file needs no connection record");
    }

    [AvaloniaFact]
    public async Task TheFirstDisk_DefaultsToBootDevice()
    {
        var first = await ReadyAsync(DiskSourceMode.ExistingFile, firstDisk: true);
        var later = await ReadyAsync(DiskSourceMode.ExistingFile, firstDisk: false);

        first.IsBoot.Should().BeTrue();
        later.IsBoot.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Luks_FinishStoresOnlyThePathOfTheKeyFile()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);
        var image = Path.Combine(_dir, "e.luks");
        var key = Path.Combine(_dir, "pass.txt");
        File.WriteAllText(image, "x");
        File.WriteAllText(key, "hunter2");
        sut.Path = image;
        sut.SelectedFormat = sut.AvailableFormats.First(f => f.Format == DiskFormat.Luks);
        sut.KeySecretFile = key;
        sut.NextCommand.Execute(null);
        sut.NextCommand.Execute(null);

        await sut.FinishCommand.ExecuteAsync(null);

        sut.Result!.ConnectionJson.Should().Contain("pass.txt").And.NotContain("hunter2");
    }

    [AvaloniaFact]
    public async Task Cancel_LeavesNoResultAndCloses()
    {
        var sut = await ReadyAsync(DiskSourceMode.ExistingFile);
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;

        sut.CancelCommand.Execute(null);

        closed.Should().BeTrue();
        sut.Result.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Create_WhenQemuImgCannotRun_ReportsTheErrorAndKeepsTheWizardOpen()
    {
        var sut = CreateSut();
        await sut.InitializeAsync(new QemuInstallation { BinaryDirectory = Path.Combine(_dir, "no-such-qemu") }, true);
        sut.Path = Path.Combine(_dir, "new.qcow2");
        var closed = false;
        sut.CloseRequested += (_, _) => closed = true;

        await sut.FinishCommand.ExecuteAsync(null);

        closed.Should().BeFalse();
        sut.Result.Should().BeNull();
        sut.StatusMessage.Should().Contain("qemu-img");
        sut.IsBusy.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Browse_UsesSaveModeOnlyWhenCreating()
    {
        var modes = new List<bool>();
        var sut = await ReadyAsync(DiskSourceMode.CreateNew);
        sut.FilePicker = (save, _) => { modes.Add(save); return Task.FromResult<string?>(Path.Combine(_dir, "x.qcow2")); };

        await sut.BrowsePathCommand.ExecuteAsync(null);
        sut.SelectedSource = Source(DiskSourceMode.ExistingFile);
        await sut.BrowsePathCommand.ExecuteAsync(null);

        modes.Should().Equal(true, false);
    }
}
