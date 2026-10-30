using Avalonia.Controls;

namespace QemuXplorer.App.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ViewModels.DashboardViewModel vm)
            await vm.RefreshCommand.ExecuteAsync(null);
    }
}
