using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using QemuXplorer.App.ViewModels;

namespace QemuXplorer.App.Views;

public partial class VmEditView : Window
{
    public VmEditView() => InitializeComponent();

    private async void OnEditDiskClicked(object? sender, RoutedEventArgs e) => await EditSelectedDiskAsync();

    private async void OnDiskDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedDiskAsync();

    private async Task EditSelectedDiskAsync()
    {
        if (DataContext is not VmEditViewModel editVm || editVm.SelectedDisk is not { } disk || App.Services is not { } services)
            return;

        var editVmDisk = services.GetRequiredService<DiskEditViewModel>();
        await editVmDisk.InitializeAsync(disk, editVm.DefaultQemu);

        var dialog = new DiskEditView { DataContext = editVmDisk };
        editVmDisk.CloseRequested += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);

        if (editVmDisk.Result is { } edited)
            editVm.ReplaceDisk(disk, edited);
    }

    private async void OnAddNetworkClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VmEditViewModel editVm || App.Services is not { } services)
            return;

        var dialogVm = services.GetRequiredService<NetworkAdapterViewModel>();
        dialogVm.InitializeForAdd(editVm.NetworkAdapters);
        var dialog = new NetworkAdapterView { DataContext = dialogVm };
        dialogVm.CloseRequested += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);

        if (dialogVm.Result is { } adapter)
            editVm.AddNetworkAdapter(adapter);
    }

    private async void OnEditNetworkClicked(object? sender, RoutedEventArgs e) => await EditSelectedNetworkAsync();

    private async void OnNetworkDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedNetworkAsync();

    private async Task EditSelectedNetworkAsync()
    {
        if (DataContext is not VmEditViewModel editVm || editVm.SelectedNetworkAdapter is not { } adapter || App.Services is not { } services)
            return;

        var dialogVm = services.GetRequiredService<NetworkAdapterViewModel>();
        dialogVm.InitializeForEdit(adapter);
        var dialog = new NetworkAdapterView { DataContext = dialogVm };
        dialogVm.CloseRequested += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);

        if (dialogVm.Result is { } edited)
            editVm.ReplaceNetworkAdapter(adapter, edited);
    }

    private async void OnAddUsbClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VmEditViewModel editVm || App.Services is not { } services)
            return;

        var dialogVm = services.GetRequiredService<UsbDeviceViewModel>();
        dialogVm.InitializeForAdd();
        var dialog = new UsbDeviceView { DataContext = dialogVm };
        dialogVm.CloseRequested += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);

        if (dialogVm.Result is { } device)
            editVm.AddUsbDevice(device);
    }

    private async void OnEditUsbClicked(object? sender, RoutedEventArgs e) => await EditSelectedUsbAsync();

    private async void OnUsbDoubleTapped(object? sender, TappedEventArgs e) => await EditSelectedUsbAsync();

    private async Task EditSelectedUsbAsync()
    {
        if (DataContext is not VmEditViewModel editVm || editVm.SelectedUsbDevice is not { } device || App.Services is not { } services)
            return;

        var dialogVm = services.GetRequiredService<UsbDeviceViewModel>();
        dialogVm.InitializeForEdit(device);
        if (device.DeviceType == Models.Enums.UsbDeviceType.HostPassthrough)
            _ = dialogVm.LoadHostDevicesAsync();
        var dialog = new UsbDeviceView { DataContext = dialogVm };
        dialogVm.CloseRequested += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);

        if (dialogVm.Result is { } edited)
            editVm.ReplaceUsbDevice(device, edited);
    }

    private async void OnAddDiskClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VmEditViewModel editVm || App.Services is not { } services)
            return;

        var wizardVm = services.GetRequiredService<DiskWizardViewModel>();
        await wizardVm.InitializeAsync(editVm.DefaultQemu, isFirstDisk: editVm.Disks.Count == 0);

        var wizard = new DiskWizardView { DataContext = wizardVm };
        wizardVm.CloseRequested += (_, _) => wizard.Close();
        await wizard.ShowDialog(this);

        if (wizardVm.Result is { } disk)
            editVm.AddDisk(disk);
    }
}
