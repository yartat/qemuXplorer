using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QEmuXlorer.Core.Services;
using QEmuXlorer.Data.Repositories;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.App.ViewModels;

public partial class QemuInstallationsViewModel : ViewModelBase
{
    private readonly IQemuInstallationRepository _repo;
    private readonly QemuDiscoveryService _discovery;

    public ObservableCollection<QemuInstallation> Installations { get; } = [];

    [ObservableProperty]
    private QemuInstallation? _selectedInstallation;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public QemuInstallationsViewModel(
        IQemuInstallationRepository repo,
        QemuDiscoveryService discovery)
    {
        _repo = repo;
        _discovery = discovery;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var list = await _repo.GetAllAsync();
        Installations.Clear();
        foreach (var i in list) Installations.Add(i);
    }

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        StatusMessage = "Detecting QEMU installations…";
        var found = await _discovery.DiscoverAsync();
        int added = 0;
        foreach (var inst in found)
        {
            await _repo.AddAsync(inst);
            Installations.Add(inst);
            added++;
        }
        StatusMessage = added > 0 ? $"Found {added} installation(s)." : "No QEMU installations found.";
    }

    [RelayCommand]
    private async Task SetDefaultAsync(QemuInstallation? inst)
    {
        if (inst is null) return;
        await _repo.SetDefaultAsync(inst.Id);
        foreach (var i in Installations) i.IsDefault = i.Id == inst.Id;
        StatusMessage = $"Default set to: {inst.Name}";
    }

    [RelayCommand]
    private async Task DeleteAsync(QemuInstallation? inst)
    {
        if (inst is null) return;
        await _repo.DeleteAsync(inst.Id);
        Installations.Remove(inst);
        if (SelectedInstallation == inst) SelectedInstallation = null;
    }
}
