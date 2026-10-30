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
