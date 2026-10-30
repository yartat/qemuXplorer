using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.Core.Services;
using QemuXplorer.Core.Tests.TestSupport;
using QemuXplorer.Models;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class DiskImageEditingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("qx-edit-").FullName;
    private readonly DiskImageService _sut = new(NullLogger<DiskImageService>.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string P(string name) => Path.Combine(_dir, name);

    // ---- argument builders (no process) ----

    [Fact]
    public void Resize_Grow_HasNoShrinkFlag() =>
        DiskImageService.BuildResizeArguments("/i/a.qcow2", DiskFormat.QCow2, 2048, shrink: false)
            .Should().Equal("resize", "-f", "qcow2", "/i/a.qcow2", "2048M");

    [Fact]
    public void Resize_Shrink_PassesShrinkBeforeThePath() =>
        DiskImageService.BuildResizeArguments("/i/a.raw", DiskFormat.Raw, 512, shrink: true)
            .Should().Equal("resize", "-f", "raw", "--shrink", "/i/a.raw", "512M");

    [Fact]
    public void Resize_WithPreallocation_UsesTheLongOption() =>
        DiskImageService.BuildResizeArguments("/i/a.qcow2", DiskFormat.QCow2, 2048, false, "metadata")
            .Should().Equal("resize", "-f", "qcow2", "--preallocation=metadata", "/i/a.qcow2", "2048M");

    [Fact]
    public void Convert_Vhd_UsesTheVpcDriverNameForSourceAndTarget() =>
        DiskImageService.BuildConvertArguments("/i/a.vhd", DiskFormat.Vhd, "/i/b.qcow2", DiskFormat.QCow2)
            .Should().Equal("convert", "-f", "vpc", "-O", "qcow2", "/i/a.vhd", "/i/b.qcow2");

    [Fact]
    public void Convert_Options_AreOneDashOWithCommasDoubled() =>
        DiskImageService.BuildConvertArguments("/i/a.raw", DiskFormat.Raw, "/i/b.qcow2", DiskFormat.QCow2,
                new Dictionary<string, string> { ["preallocation"] = "metadata", ["cluster_size"] = "", ["backing_file"] = "x,y" })
            .Should().Equal("convert", "-f", "raw", "-O", "qcow2", "-o", "preallocation=metadata,backing_file=x,,y", "/i/a.raw", "/i/b.qcow2");

    [Fact]
    public void Rebase_ToANewBackingFile_NamesItsFormat() =>
        DiskImageService.BuildRebaseArguments("/i/a.qcow2", DiskFormat.QCow2, "/i/base.raw", DiskFormat.Raw, unsafeMode: false)
            .Should().Equal("rebase", "-f", "qcow2", "-b", "/i/base.raw", "-F", "raw", "/i/a.qcow2");

    [Fact]
    public void Rebase_Unsafe_AddsDashU() =>
        DiskImageService.BuildRebaseArguments("/i/a.qed", DiskFormat.Qed, "/i/base.raw", DiskFormat.Raw, unsafeMode: true)
            .Should().Equal("rebase", "-f", "qed", "-u", "-b", "/i/base.raw", "-F", "raw", "/i/a.qed");

    [Fact]
    public void Rebase_Flatten_PassesAnEmptyBackingFileAndNoFormat() =>
        DiskImageService.BuildRebaseArguments("/i/a.qcow2", DiskFormat.QCow2, null, DiskFormat.Raw, unsafeMode: true)
            .Should().Equal("rebase", "-f", "qcow2", "-b", "", "/i/a.qcow2");

    [Fact]
    public void Amend_PutsOptionsInOneDashO() =>
        DiskImageService.BuildAmendArguments("/i/a.qcow2", DiskFormat.QCow2,
                new Dictionary<string, string> { ["compat"] = "1.1", ["lazy_refcounts"] = "on" })
            .Should().Equal("amend", "-f", "qcow2", "-o", "compat=1.1,lazy_refcounts=on", "/i/a.qcow2");

    [Theory]
    [InlineData(null, new[] { "check", "-f", "qcow2", "/i/a.qcow2" })]
    [InlineData("leaks", new[] { "check", "-f", "qcow2", "-r", "leaks", "/i/a.qcow2" })]
    [InlineData("all", new[] { "check", "-f", "qcow2", "-r", "all", "/i/a.qcow2" })]
    public void Check_RepairIsOptional(string? repair, string[] expected) =>
        DiskImageService.BuildCheckArguments("/i/a.qcow2", DiskFormat.QCow2, repair).Should().Equal(expected);

    // ---- capabilities of the formats ----

    [Theory]
    [InlineData(DiskFormat.Raw, true, true, false, false, false)]
    [InlineData(DiskFormat.QCow2, true, true, true, true, true)]
    [InlineData(DiskFormat.Qed, true, false, true, false, true)]
    [InlineData(DiskFormat.Qcow, false, false, false, false, false)]
    [InlineData(DiskFormat.Vdi, false, false, false, false, true)]
    [InlineData(DiskFormat.Vmdk, false, false, false, false, true)]
    [InlineData(DiskFormat.Vhd, false, false, false, false, false)]
    [InlineData(DiskFormat.Vhdx, false, false, false, false, true)]
    public void FormatCapabilities_MatchWhatQemuImgDoes(
        DiskFormat format, bool resize, bool shrink, bool rebase, bool amend, bool check)
    {
        var info = DiskFormatCatalog.Get(format);

        (info.CanResize, info.CanShrink, info.CanRebase, info.CanAmend, info.CanCheck)
            .Should().Be((resize, shrink, rebase, amend, check));
    }

    [Theory]
    [InlineData(DiskFormat.Bochs)]
    [InlineData(DiskFormat.Cloop)]
    [InlineData(DiskFormat.Dmg)]
    public void ReadOnlyFormats_CanOnlyBeConverted(DiskFormat format)
    {
        var info = DiskFormatCatalog.Get(format);

        (info.CanResize || info.CanRebase || info.CanAmend || info.CanCheck).Should().BeFalse();
    }

    // ---- running (with a stand-in qemu-img) ----

    // Writes a script that ignores its arguments except that, when asked, it creates the file named by
    // the last one (the convert destination), then prints something and exits with the wanted code.
    private string FakeQemuImg(bool succeed, bool createsOutput)
    {
        var fake = Path.Combine(_dir, OperatingSystem.IsWindows() ? "fake.cmd" : "fake");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllLines(fake,
            [
                "@echo off",
                "set last=%~6",
                createsOutput ? "type nul > \"%last%\"" : "rem",
                succeed ? "echo all good" : "echo it broke 1>&2",
                succeed ? "exit /b 0" : "exit /b 1"
            ]);
        }
        else
        {
            File.WriteAllLines(fake,
            [
                "#!/bin/sh",
                createsOutput ? ": > \"$6\"" : ":",
                succeed ? "echo all good" : "echo it broke >&2",
                succeed ? "exit 0" : "exit 1"
            ]);
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return fake;
    }

    [Fact]
    public async Task Run_Success_ReturnsWhatQemuImgPrinted()
    {
        var op = new DiskImageOperation("x", ["resize", "-f", "raw", "a", "1M"]);

        var output = await _sut.RunAsync(FakeQemuImg(succeed: true, createsOutput: false), op);

        output.Should().Be("all good");
    }

    [Fact]
    public async Task Run_Failure_ThrowsWithQemuImgsOwnMessage()
    {
        var op = new DiskImageOperation("x", ["resize", "-f", "raw", "a", "1M"]);

        var act = () => _sut.RunAsync(FakeQemuImg(succeed: false, createsOutput: false), op);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*qemu-img resize failed: it broke*");
    }

    [Fact]
    public async Task Run_FailedConvert_RemovesTheHalfWrittenOutput()
    {
        var destination = P("out.qcow2");
        var op = new DiskImageOperation("convert", ["convert", "-f", "raw", "-O", "qcow2", destination], OutputPath: destination);

        var act = () => _sut.RunAsync(FakeQemuImg(succeed: false, createsOutput: true), op);

        await act.Should().ThrowAsync<InvalidOperationException>();
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task Run_SuccessfulConvert_KeepsTheOutput()
    {
        var destination = P("out.qcow2");
        var op = new DiskImageOperation("convert", ["convert", "-f", "raw", "-O", "qcow2", destination], OutputPath: destination);

        await _sut.RunAsync(FakeQemuImg(succeed: true, createsOutput: true), op);

        File.Exists(destination).Should().BeTrue();
    }

    // ---- against the real qemu-img, where it is installed ----

    private async Task<string> Real(params string[] args)
        => await _sut.RunAsync(RealQemuImg.Path!, new DiskImageOperation("real", args));

    [RealQemuImgFact]
    public async Task Real_ResizeGrowAndShrink_ChangeTheVirtualSize()
    {
        var path = P("a.qcow2");
        await Real("create", "-f", "qcow2", path, "64M");

        await Real(DiskImageService.BuildResizeArguments(path, DiskFormat.QCow2, 128, false).ToArray());
        (await _sut.GetImageInfoAsync(RealQemuImg.Path!, path, format: DiskFormat.QCow2))!.SizeBytes.Should().Be(128L << 20);

        await Real(DiskImageService.BuildResizeArguments(path, DiskFormat.QCow2, 96, true).ToArray());
        (await _sut.GetImageInfoAsync(RealQemuImg.Path!, path, format: DiskFormat.QCow2))!.SizeBytes.Should().Be(96L << 20);
    }

    [RealQemuImgFact]
    public async Task Real_ShrinkWithoutTheFlag_IsRefused()
    {
        var path = P("a.raw");
        await Real("create", "-f", "raw", path, "64M");

        var act = () => Real(DiskImageService.BuildResizeArguments(path, DiskFormat.Raw, 32, shrink: false).ToArray());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*--shrink*");
    }

    [RealQemuImgFact]
    public async Task Real_Convert_WritesTheNewFormatAndKeepsTheOriginal()
    {
        var source = P("a.raw");
        var target = P("b.qcow2");
        await Real("create", "-f", "raw", source, "16M");

        await Real(DiskImageService.BuildConvertArguments(source, DiskFormat.Raw, target, DiskFormat.QCow2).ToArray());

        File.Exists(source).Should().BeTrue();
        (await _sut.GetImageInfoAsync(RealQemuImg.Path!, target, format: DiskFormat.QCow2))!.Format.Should().Be(DiskFormat.QCow2);
    }

    [RealQemuImgFact]
    public async Task Real_Rebase_ChangesAndRemovesTheBackingFile()
    {
        var first = P("first.raw");
        var second = P("second.raw");
        var layer = P("layer.qcow2");
        await Real("create", "-f", "raw", first, "8M");
        await Real("create", "-f", "raw", second, "8M");
        await Real("create", "-f", "qcow2", "-b", first, "-F", "raw", layer, "8M");

        await Real(DiskImageService.BuildRebaseArguments(layer, DiskFormat.QCow2, second, DiskFormat.Raw, false).ToArray());
        (await _sut.GetImageInfoAsync(RealQemuImg.Path!, layer, format: DiskFormat.QCow2))!.BackingFile.Should().EndWith("second.raw");

        await Real(DiskImageService.BuildRebaseArguments(layer, DiskFormat.QCow2, null, null, false).ToArray());
        (await _sut.GetImageInfoAsync(RealQemuImg.Path!, layer, format: DiskFormat.QCow2))!.BackingFile.Should().BeNull();
    }

    [RealQemuImgFact]
    public async Task Real_AmendAndCheck_AcceptTheOptionsTheDialogOffers()
    {
        var path = P("a.qcow2");
        await Real("create", "-f", "qcow2", "-o", "compat=1.1", path, "8M");

        await Real(DiskImageService.BuildAmendArguments(path, DiskFormat.QCow2,
            new Dictionary<string, string> { ["compat"] = "1.1", ["lazy_refcounts"] = "on", ["refcount_bits"] = "8" }).ToArray());
        var report = await Real(DiskImageService.BuildCheckArguments(path, DiskFormat.QCow2).ToArray());

        report.Should().Contain("No errors were found");
    }

    [RealQemuImgFact]
    public async Task Real_FixedVhdInfo_IsCorrectOnceTheFormatIsPassed()
    {
        var path = P("a.vhd");
        await Real("create", "-f", "vpc", "-o", "subformat=fixed", path, "8M");

        var probed = await _sut.GetImageInfoAsync(RealQemuImg.Path!, path);
        var named = await _sut.GetImageInfoAsync(RealQemuImg.Path!, path, format: DiskFormat.Vhd);

        probed!.Format.Should().Be(DiskFormat.Raw, "this is the misreport the format argument exists to avoid");
        named!.Format.Should().Be(DiskFormat.Vhd);
    }
}
