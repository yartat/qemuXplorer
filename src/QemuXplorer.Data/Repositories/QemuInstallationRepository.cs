using Microsoft.EntityFrameworkCore;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.Data.Repositories;

public sealed class QemuInstallationRepository : IQemuInstallationRepository
{
    private readonly IDbContextFactory<QemuXplorerDbContext> _factory;

    public QemuInstallationRepository(IDbContextFactory<QemuXplorerDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<QemuInstallation>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.QemuInstallations.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<QemuInstallation?> GetDefaultAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.QemuInstallations.AsNoTracking().FirstOrDefaultAsync(x => x.IsDefault, ct);
    }

    public async Task AddAsync(QemuInstallation installation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.QemuInstallations.Add(installation);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(QemuInstallation installation, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.QemuInstallations.Update(installation);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var entry = await db.QemuInstallations.FindAsync([id], ct);
        if (entry is not null)
        {
            db.QemuInstallations.Remove(entry);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.QemuInstallations
            .Where(x => x.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);

        await db.QemuInstallations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, true), ct);

        await tx.CommitAsync(ct);
    }

    public async Task<bool> ExistsAsync(string binaryDirectory, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.QemuInstallations.AnyAsync(x => x.BinaryDirectory == binaryDirectory, ct);
    }
}
