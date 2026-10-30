using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using QemuXplorer.App.ViewModels;
using QemuXplorer.App.Views;
using QemuXplorer.Core.Services;
using QemuXplorer.Models.Entities;
using QemuXplorer.Models.Enums;
using Xunit;

namespace QemuXplorer.App.Tests;

/// <summary>Drives the real windows with real mouse input: a double-click on a type moves on like Next does.</summary>
public class WizardDoubleClickTests
{
    private static void DoubleClick(Window window, Visual target)
    {
        var centre = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("The item is not on screen.");
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
        }
    }

    private static Window Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static ListBoxItem ItemWithText(Window window, string text) =>
        window.GetVisualDescendants().OfType<ListBoxItem>()
            .First(item => item.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));

    [AvaloniaFact]
    public void Network_DoubleClickingAType_ActsLikeNext()
    {
        var vm = new NetworkAdapterViewModel();
        vm.InitializeForAdd([]);
        var window = Show(new NetworkAdapterView { DataContext = vm });

        DoubleClick(window, ItemWithText(window, "Tap interface"));

        vm.SelectedBackend.Type.Should().Be(NetworkBackendType.Tap);
        vm.IsStep1.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void Network_ASingleClick_OnlySelects()
    {
        var vm = new NetworkAdapterViewModel();
        vm.InitializeForAdd([]);
        var window = Show(new NetworkAdapterView { DataContext = vm });
        var item = ItemWithText(window, "Bridge");
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

        vm.SelectedBackend.Type.Should().Be(NetworkBackendType.Bridge);
        vm.IsStep0.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void Network_DoubleClickingWhileEditing_DoesNothing()
    {
        // Editing has no type step, so there is nothing to advance from.
        var vm = new NetworkAdapterViewModel();
        vm.InitializeForEdit(new NetworkAdapter { BackendType = NetworkBackendType.Socket });
        var window = Show(new NetworkAdapterView { DataContext = vm });

        vm.IsStep1.Should().BeTrue();
        window.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.IsEffectivelyVisible).Should().BeEmpty();
        window.Close();
    }

    [AvaloniaFact]
    public void Usb_DoubleClickingAType_ActsLikeNext()
    {
        var vm = new UsbDeviceViewModel(new UsbHostService(NullLogger<UsbHostService>.Instance));
        vm.InitializeForAdd();
        var window = Show(new UsbDeviceView { DataContext = vm });

        DoubleClick(window, ItemWithText(window, "Tablet (absolute pointer)"));

        vm.SelectedType.Type.Should().Be(UsbDeviceType.Tablet);
        vm.IsStep1.Should().BeTrue();
        window.Close();
    }

    [AvaloniaFact]
    public void Usb_ASingleClick_OnlySelects()
    {
        var vm = new UsbDeviceViewModel(new UsbHostService(NullLogger<UsbHostService>.Instance));
        vm.InitializeForAdd();
        var window = Show(new UsbDeviceView { DataContext = vm });
        var item = ItemWithText(window, "Keyboard");
        var centre = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);

        vm.SelectedType.Type.Should().Be(UsbDeviceType.Keyboard);
        vm.IsStep0.Should().BeTrue();
        window.Close();
    }
}
