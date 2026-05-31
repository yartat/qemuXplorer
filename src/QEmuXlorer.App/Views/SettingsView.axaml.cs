using Avalonia.Controls;

namespace QEmuXlorer.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    protected override async void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is ViewModels.SettingsViewModel vm)
            await vm.LoadCommand.ExecuteAsync(null);
    }
}
