using Avalonia.Controls;

namespace QEmuXlorer.App.Views;

public partial class QemuInstallationsView : UserControl
{
    public QemuInstallationsView() => InitializeComponent();

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ViewModels.QemuInstallationsViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
