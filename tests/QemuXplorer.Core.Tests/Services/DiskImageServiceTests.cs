using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class DiskImageServiceTests
{
    [Fact]
    public void BuildCreateArguments_Qcow2WithOptions_PutsOptionsInOneDashO()
    {
        var args = DiskImageService.BuildCreateArguments(new DiskCreateRequest(
            "/img/a.qcow2", DiskFormat.QCow2, 20480,
            new Dictionary<string, string> { ["preallocation"] = "metadata", ["cluster_size"] = "65536" }));

        args.Should().Equal("create", "-f", "qcow2", "-o", "preallocation=metadata,cluster_size=65536", "/img/a.qcow2", "20480M");
    }

    [Fact]
    public void BuildCreateArguments_NoOptions_OmitsDashO()
    {
        var args = DiskImageService.BuildCreateArguments(new DiskCreateRequest("/i/a.raw", DiskFormat.Raw, 100));

        args.Should().Equal("create", "-f", "raw", "/i/a.raw", "100M");
    }

    [Fact]
    public void BuildCreateArguments_BlankOptionValues_AreDropped()
    {
        var args = DiskImageService.BuildCreateArguments(new DiskCreateRequest(
            "/i/a.vdi", DiskFormat.Vdi, 10, new Dictionary<string, string> { ["static"] = "" }));

        args.Should().NotContain("-o");
    }

    [Fact]
    public void BuildCreateArguments_Vhd_UsesTheVpcDriverName()
    {
        DiskImageService.BuildCreateArguments(new DiskCreateRequest("/i/a.vhd", DiskFormat.Vhd, 10))
            .Should().ContainInOrder("-f", "vpc");
    }

    [Fact]
    public void BuildCreateArguments_OptionValueWithComma_IsEscaped()
    {
        var args = DiskImageService.BuildCreateArguments(new DiskCreateRequest(
            "/i/a.qcow2", DiskFormat.QCow2, 10,
            new Dictionary<string, string> { ["backing_file"] = "/base,v2.qcow2" }));

        args.Should().Contain("backing_file=/base,,v2.qcow2");
    }

    [Fact]
    public void BuildCreateArguments_Luks_ReferencesASecretFileNotAPassword()
    {
        var args = DiskImageService.BuildCreateArguments(new DiskCreateRequest(
            "/i/a.luks", DiskFormat.Luks, 10, KeySecretFile: "/keys/pass.txt"));

        args.Should().ContainInOrder("--object", "secret,id=sec0,file=/keys/pass.txt");
        args.Should().Contain("key-secret=sec0");
    }

    [Fact]
    public void BuildCreateArguments_LuksWithoutKeyFile_Throws()
    {
        var act = () => DiskImageService.BuildCreateArguments(new DiskCreateRequest("/i/a.luks", DiskFormat.Luks, 10));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CreateImageAsync_ExistingFile_IsNeverOverwritten()
    {
        var path = Path.GetTempFileName();
        try
        {
            var sut = new DiskImageService(NullLogger<DiskImageService>.Instance);

            var act = () => sut.CreateImageAsync("qemu-img", new DiskCreateRequest(path, DiskFormat.Raw, 1));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task CreateImageAsync_WhenQemuImgFailsAfterCreatingTheFile_RemovesThePartialFile()
    {
        var dir = Directory.CreateTempSubdirectory("qx-img-").FullName;
        try
        {
            // A stand-in qemu-img that creates the target (its 4th argument), then fails.
            var fake = Path.Combine(dir, OperatingSystem.IsWindows() ? "fake-qemu-img.cmd" : "fake-qemu-img");
            if (OperatingSystem.IsWindows())
                File.WriteAllLines(fake, ["@echo off", "type nul > \"%~4\"", "echo Unsupported preallocation mode 1>&2", "exit /b 1"]);
            else
            {
                File.WriteAllLines(fake, ["#!/bin/sh", ": > \"$4\"", "echo Unsupported preallocation mode >&2", "exit 1"]);
                File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            var target = Path.Combine(dir, "a.raw");
            var sut = new DiskImageService(NullLogger<DiskImageService>.Instance);

            var act = () => sut.CreateImageAsync(fake, new DiskCreateRequest(target, DiskFormat.Raw, 1));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unsupported preallocation mode*");
            File.Exists(target).Should().BeFalse("a failed attempt must not block the next one with 'already exists'");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParseSupportedDrivers_ReadsTheFormatListAndStopsAtProse()
    {
        const string help = "usage: qemu-img ...\n\nSupported image formats:\n\n  blkdebug bochs cloop nbd qcow2\n  raw ssh vdi\n\nSee <https://qemu.org/contribute/report-a-bug> for how to report bugs.\n";

        var drivers = DiskImageService.ParseSupportedDrivers(help);

        drivers.Should().BeEquivalentTo("blkdebug", "bochs", "cloop", "nbd", "qcow2", "raw", "ssh", "vdi");
        drivers.Should().NotContain("iscsi");
    }

    [Fact]
    public void ResolveQemuImg_LivesInTheInstallationDirectory()
    {
        DiskImageService.ResolveQemuImg("/opt/qemu").Should().StartWith(Path.Combine("/opt/qemu", "qemu-img"));
    }
}
