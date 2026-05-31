using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using QEmuXlorer.App.ViewModels;

namespace QEmuXlorer.App.Views;

public partial class VirtualMachinesView : UserControl
{
    public VirtualMachinesView()
    {
        InitializeComponent();
    }

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is VirtualMachinesViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }

    private async void OnEditClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not VirtualMachinesViewModel listVm) return;
        var selected = listVm.SelectedVm;
        if (selected is null) return;

        var sp = App.Services;
        if (sp is null) return;

        var editVm = sp.GetRequiredService<VmEditViewModel>();
        editVm.LoadVm(selected);

        var window = new VmEditView { DataContext = editVm };
        editVm.CloseRequested += (_, _) => window.Close();
        await window.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("No top-level window"));
        await listVm.LoadCommand.ExecuteAsync(null);
    }
}
