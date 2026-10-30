using Avalonia.Controls;
using Avalonia.Platform.Storage;
using QemuXplorer.App.ViewModels;

namespace QemuXplorer.App.Views;

public partial class DiskWizardView : Window
{
    public DiskWizardView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DiskWizardViewModel vm)
            vm.FilePicker = PickFileAsync;
    }

    private async Task<string?> PickFileAsync(bool save, string title)
    {
        if (save)
        {
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title });
            return target?.TryGetLocalPath();
        }

        var picked = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }
}
