using Microsoft.EntityFrameworkCore;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.Data.Repositories;

public sealed class VirtualMachineRepository : IVirtualMachineRepository
{
    private readonly IDbContextFactory<QemuXplorerDbContext> _factory;

    public VirtualMachineRepository(IDbContextFactory<QemuXplorerDbContext> factory) => _factory = factory;

    // Three child collections are loaded with every machine. In one statement their rows multiply
    // (disks x adapters x USB devices per machine), so each collection is loaded by its own query.
    public async Task<IReadOnlyList<VirtualMachine>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VirtualMachines
            .Include(x => x.Disks)
            .Include(x => x.NetworkAdapters)
            .Include(x => x.UsbDevices)
            .AsSplitQuery()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
    }

    public async Task<VirtualMachine?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VirtualMachines
            .Include(x => x.Disks)
            .Include(x => x.NetworkAdapters)
            .Include(x => x.UsbDevices)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<VirtualMachine?> GetWithDisksAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.VirtualMachines
            .Include(x => x.Disks)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task AddAsync(VirtualMachine vm, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        vm.CreatedAt = DateTime.UtcNow;
        vm.ModifiedAt = DateTime.UtcNow;
        db.VirtualMachines.Add(vm);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(VirtualMachine vm, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.VirtualMachines
            .Include(x => x.Disks)
            .Include(x => x.NetworkAdapters)
            .Include(x => x.UsbDevices)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == vm.Id, ct);

        if (existing is null)
            return;

        vm.ModifiedAt = DateTime.UtcNow;
        db.Entry(existing).CurrentValues.SetValues(vm);

        // Owned types are replaced wholesale rather than merged.
        existing.Cpu = vm.Cpu;
        existing.Memory = vm.Memory;
        existing.Display = vm.Display;

        SyncChildren(db, existing.Disks, vm.Disks, x => x.Id);
        SyncChildren(db, existing.NetworkAdapters, vm.NetworkAdapters, x => x.Id);
        SyncChildren(db, existing.UsbDevices, vm.UsbDevices, x => x.Id);

        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vm = await db.VirtualMachines.FindAsync([id], ct);
        if (vm is not null)
        {
            db.VirtualMachines.Remove(vm);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Reconciles a tracked child collection against a detached one by key: rows missing from
    /// the incoming set are deleted, new rows are inserted, and matches are updated in place.
    /// Marking the whole detached graph as Modified instead would make EF issue UPDATEs for
    /// children that have no row yet.
    /// </summary>
    private static void SyncChildren<T>(
        QemuXplorerDbContext db,
        List<T> tracked,
        List<T> incoming,
        Func<T, Guid> keySelector)
        where T : class
    {
        foreach (var removed in tracked.Where(t => incoming.All(i => keySelector(i) != keySelector(t))).ToList())
        {
            tracked.Remove(removed);
            db.Remove(removed);
        }

        foreach (var item in incoming)
        {
            var match = tracked.FirstOrDefault(t => keySelector(t) == keySelector(item));
            if (match is null)
            {
                tracked.Add(item);
                // The key is a Guid the caller already set (the entity constructor does it), and EF
                // treats a row with a set key as one that exists. Left to infer it, EF would send
                // an UPDATE for a row that is not there and fail with DbUpdateConcurrencyException.
                db.Entry(item).State = EntityState.Added;
            }
            else
                db.Entry(match).CurrentValues.SetValues(item);
        }
    }
}
