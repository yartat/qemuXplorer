using FluentAssertions;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.Core.Tests.Services;

public class DiskSourceUriTests
{
    [Fact]
    public void Nbd_WithExport() => DiskSourceUri.Nbd("srv", 10809, "disk1").Should().Be("nbd://srv:10809/disk1");

    [Fact]
    public void Nbd_WithoutExport_OmitsThePath() => DiskSourceUri.Nbd("srv", 10809).Should().Be("nbd://srv:10809");

    [Fact]
    public void Nbd_Ipv6Host_IsBracketed() => DiskSourceUri.Nbd("::1", 10809).Should().Be("nbd://[::1]:10809");

    [Fact]
    public void Ssh_KeepsTheAbsolutePath() =>
        DiskSourceUri.Ssh("bob", "host", 22, "/var/img/d.qcow2").Should().Be("ssh://bob@host:22/var/img/d.qcow2");

    [Fact]
    public void Ssh_WithoutUser_OmitsTheAtSign() =>
        DiskSourceUri.Ssh(null, "host", 2222, "img/d.raw").Should().Be("ssh://host:2222/img/d.raw");

    [Fact]
    public void Ssh_EscapesSpacesInSegments() =>
        DiskSourceUri.Ssh("u", "h", 22, "/my disks/a b.img").Should().Be("ssh://u@h:22/my%20disks/a%20b.img");

    [Fact]
    public void Iscsi_PutsTargetThenLun() =>
        DiskSourceUri.Iscsi(null, "10.0.0.5", 3260, "iqn.2001-04.com.example:disk1", 1)
            .Should().Be("iscsi://10.0.0.5:3260/iqn.2001-04.com.example%3Adisk1/1");

    [Theory]
    [InlineData("nbd://h:1/x", DiskSourceKind.Nbd)]
    [InlineData("SSH://u@h/p", DiskSourceKind.Ssh)]
    [InlineData("iscsi://h/t/1", DiskSourceKind.Iscsi)]
    [InlineData("/img/a.qcow2", DiskSourceKind.LocalFile)]
    [InlineData("C:\\img\\a.qcow2", DiskSourceKind.LocalFile)]
    [InlineData("", DiskSourceKind.LocalFile)]
    public void Classify_RecognisesTheScheme(string path, DiskSourceKind expected) =>
        DiskSourceUri.Classify(path).Should().Be(expected);
}
