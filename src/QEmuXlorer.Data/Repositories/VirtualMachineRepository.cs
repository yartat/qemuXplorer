using Microsoft.EntityFrameworkCore;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data.Repositories;

public sealed class VirtualMachineRepository : IVirtualMachineRepository
{
    private readonly QEmuXplorerDbContext _db;

    public VirtualMachineRepository(QEmuXplorerDbContext db) => _db = db;

    public async Task<IReadOnlyList<VirtualMachine>> GetAllAsync(CancellationToken ct = default)
        => await _db.VirtualMachines
            .Include(x => x.Disks)
            .Include(x => x.NetworkAdapters)
            .Include(x => x.UsbDevices)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

    public async Task<VirtualMachine?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.VirtualMachines
            .Include(x => x.Disks)
            .Include(x => x.NetworkAdapters)
            .Include(x => x.UsbDevices)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<VirtualMachine?> GetWithDisksAsync(Guid id, CancellationToken ct = default)
        => await _db.VirtualMachines
            .Include(x => x.Disks)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task AddAsync(VirtualMachine vm, CancellationToken ct = default)
    {
        vm.CreatedAt = DateTime.UtcNow;
        vm.ModifiedAt = DateTime.UtcNow;
        _db.VirtualMachines.Add(vm);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(VirtualMachine vm, CancellationToken ct = default)
    {
        vm.ModifiedAt = DateTime.UtcNow;
        _db.VirtualMachines.Update(vm);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var vm = await _db.VirtualMachines.FindAsync([id], ct);
        if (vm is not null)
        {
            _db.VirtualMachines.Remove(vm);
            await _db.SaveChangesAsync(ct);
        }
    }
}
