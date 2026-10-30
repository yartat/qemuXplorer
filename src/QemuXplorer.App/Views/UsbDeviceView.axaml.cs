using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using QemuXplorer.App.ViewModels;

namespace QemuXplorer.App.Views;

public partial class UsbDeviceView : Window
{
    public UsbDeviceView() => InitializeComponent();

    // A double-click on a type does what Next does; one on the empty part of the list does nothing.
    private void OnTypeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is UsbDeviceViewModel { IsStep0: true } vm && (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>() is not null)
            vm.NextCommand.Execute(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is UsbDeviceViewModel vm)
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
