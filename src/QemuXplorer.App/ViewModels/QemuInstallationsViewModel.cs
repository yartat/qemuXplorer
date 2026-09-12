using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QemuXplorer.Core.Services;
using QemuXplorer.Data.Repositories;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.App.ViewModels;

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
        var selectedId = SelectedInstallation?.Id;

        var list = await _repo.GetAllAsync();
        Installations.Clear();
        foreach (var i in list) Installations.Add(i);

        SelectedInstallation = selectedId is null
            ? null
            : Installations.FirstOrDefault(i => i.Id == selectedId);
    }

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        StatusMessage = "Detecting QEMU installations…";

        var found = await _discovery.DiscoverAsync();
        var added = 0;

        foreach (var inst in found)
        {
            // Re-running detection must not pile up duplicate rows for the same directory.
            if (await _repo.ExistsAsync(inst.BinaryDirectory)) continue;

            await _repo.AddAsync(inst);
            added++;
        }

        await LoadAsync();

        // A first successful detection is useless without a default to launch from.
        if (added > 0 && Installations.All(i => !i.IsDefault))
        {
            await _repo.SetDefaultAsync(Installations[0].Id);
            await LoadAsync();
        }

        StatusMessage = added > 0
            ? $"Added {added} installation(s)."
            : found.Count > 0
                ? "No new installations — all detected paths are already registered."
                : "No QEMU installations found.";
    }

    [RelayCommand]
    private async Task SetDefaultAsync(QemuInstallation? inst)
    {
        if (inst is null) return;

        await _repo.SetDefaultAsync(inst.Id);
        // The entities are plain POCOs, so the grid only reflects the change after a reload.
        await LoadAsync();
        StatusMessage = $"Default set to: {inst.Name}";
    }

    [RelayCommand]
    private async Task DeleteAsync(QemuInstallation? inst)
    {
        if (inst is null) return;

        await _repo.DeleteAsync(inst.Id);
        await LoadAsync();
        StatusMessage = $"Removed: {inst.Name}";
    }
}
