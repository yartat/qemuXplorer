using Microsoft.EntityFrameworkCore;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data.Repositories;

public sealed class QemuInstallationRepository : IQemuInstallationRepository
{
    private readonly QEmuXplorerDbContext _db;

    public QemuInstallationRepository(QEmuXplorerDbContext db) => _db = db;

    public async Task<IReadOnlyList<QemuInstallation>> GetAllAsync(CancellationToken ct = default)
        => await _db.QemuInstallations.OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<QemuInstallation?> GetDefaultAsync(CancellationToken ct = default)
        => await _db.QemuInstallations.FirstOrDefaultAsync(x => x.IsDefault, ct);

    public async Task AddAsync(QemuInstallation installation, CancellationToken ct = default)
    {
        _db.QemuInstallations.Add(installation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(QemuInstallation installation, CancellationToken ct = default)
    {
        _db.QemuInstallations.Update(installation);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entry = await _db.QemuInstallations.FindAsync([id], ct);
        if (entry is not null)
        {
            _db.QemuInstallations.Remove(entry);
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        // Clear existing default, then set new one
        await _db.QemuInstallations
            .Where(x => x.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);

        await _db.QemuInstallations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, true), ct);
    }
}
