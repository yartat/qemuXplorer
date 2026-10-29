using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using FluentAssertions;
using QemuXplorer.App.Views;
using Xunit;

namespace QemuXplorer.App.Tests.Views;

/// <summary>
/// A window with no Icon shows the generic Avalonia glyph in the title bar and taskbar, and
/// nothing fails to say so. Covering every Window by reflection means a dialog added later
/// cannot forget it.
/// </summary>
public class AppIconTests
{
    private static readonly Uri WindowIcon = new("avares://QemuXplorer/Assets/icon-256.png");

    public static TheoryData<Type> WindowTypes
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in typeof(MainWindow).Assembly.GetTypes()
                         .Where(t => t is { IsAbstract: false, IsPublic: true }
                                     && typeof(Window).IsAssignableFrom(t)
                                     && t.GetConstructor(Type.EmptyTypes) is not null)
                         .OrderBy(t => t.Name))
                data.Add(type);
            return data;
        }
    }

    [AvaloniaFact]
    public void WindowTypes_AreActuallyDiscovered()
    {
        // MainWindow, VmEditView and DiskWizardView at least.
        WindowTypes.Count().Should().BeGreaterThanOrEqualTo(3);
    }

    [AvaloniaTheory]
    [MemberData(nameof(WindowTypes))]
    public void Window_HasTheApplicationIcon(Type windowType)
    {
        var window = (Window)Activator.CreateInstance(windowType)!;

        window.Icon.Should().NotBeNull($"{windowType.Name} should set Icon in its XAML");
    }

    [AvaloniaFact]
    public void IconResource_IsA256PixelPng()
    {
        AssetLoader.Exists(WindowIcon).Should().BeTrue("the PNG must be embedded as an AvaloniaResource");

        using var stream = AssetLoader.Open(WindowIcon);
        var header = new byte[24];
        stream.ReadExactly(header);

        header[..8].Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        // IHDR: width and height are big-endian uint32 at offsets 16 and 20.
        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        (width, height).Should().Be((256, 256));
    }
}
