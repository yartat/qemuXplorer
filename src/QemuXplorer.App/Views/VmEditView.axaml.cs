using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using QemuXplorer.App.ViewModels;

namespace QemuXplorer.App.Views;

public partial class VmEditView : Window
{
    public VmEditView() => InitializeComponent();

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
