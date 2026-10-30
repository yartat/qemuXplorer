using System.Buffers.Binary;
using FluentAssertions;
using Xunit;

namespace QemuXplorer.App.Tests.Views;

/// <summary>
/// Structural checks on the generated icon files. A malformed .ico or .icns does not fail the
/// build; it just shows a blank or generic icon on the platform that cannot read it, which is
/// only noticed after release. These read the committed files straight from the repository.
/// </summary>
public class IconFilesTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string AssetsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QemuXplorer.sln")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the test must run from inside the repository");
        return Path.Combine(dir!.FullName, "src", "QemuXplorer.App", "Assets");
    }

    private static (int Width, int Height) PngSize(ReadOnlySpan<byte> png)
    {
        png[..8].SequenceEqual(PngSignature).Should().BeTrue("the payload must be a PNG");
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]));
    }

    [Fact]
    public void Ico_ContainsTheExpectedSizes_EachAValidPngOfThatSize()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AssetsDirectory(), "icon.ico"));

        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)).Should().Be(0, "reserved");
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)).Should().Be(1, "type 1 is an icon");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            // A stored 0 means 256.
            var declared = bytes[entry] == 0 ? 256 : bytes[entry];
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12));

            (offset + length).Should().BeLessThanOrEqualTo(bytes.Length);
            PngSize(bytes.AsSpan(offset, length)).Should().Be((declared, declared));
            sizes.Add(declared);
        }

        sizes.Should().Equal(16, 24, 32, 48, 64, 128, 256);
    }

    [Fact]
    public void Icns_HasEveryBlockAtTheRightSize_AndItsLengthsAddUp()
    {
        // Block type -> pixel size, as `iconutil` writes them.
        var expected = new Dictionary<string, int>
        {
            ["icp4"] = 16, ["ic11"] = 32, ["icp5"] = 32, ["ic12"] = 64, ["ic07"] = 128,
            ["ic13"] = 256, ["ic08"] = 256, ["ic14"] = 512, ["ic09"] = 512, ["ic10"] = 1024
        };
        var bytes = File.ReadAllBytes(Path.Combine(AssetsDirectory(), "icon.icns"));

        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("icns");
        ((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4))).Should().Be(bytes.Length, "the header length covers the whole file");

        var seen = new List<string>();
        var position = 8;
        while (position < bytes.Length)
        {
            var type = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 4));
            length.Should().BeGreaterThan(8);

            expected.Should().ContainKey(type);
            PngSize(bytes.AsSpan(position + 8, length - 8)).Should().Be((expected[type], expected[type]), type);

            seen.Add(type);
            position += length;
        }

        position.Should().Be(bytes.Length, "the blocks must end exactly at the end of the file");
        seen.Should().BeEquivalentTo(expected.Keys);
    }

    [Fact]
    public void Svg_IsTheMasterDrawing()
    {
        var svg = File.ReadAllText(Path.Combine(AssetsDirectory(), "icon.svg"));

        svg.Should().Contain("viewBox=\"0 0 256 256\"");
        svg.Should().Contain("#f5a524", "the lens is the amber accent");
    }
}
