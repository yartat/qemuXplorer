using Microsoft.EntityFrameworkCore;
using QemuXplorer.Models.Entities;

namespace QemuXplorer.Data.Repositories;

public sealed class AppSettingRepository : IAppSettingRepository
{
    private readonly IDbContextFactory<QemuXplorerDbContext> _factory;

    public AppSettingRepository(IDbContextFactory<QemuXplorerDbContext> factory) => _factory = factory;

    public async Task<AppSetting?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var setting = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.AppSettings.FindAsync([key], ct);
        if (existing is null)
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AppSettings.AsNoTracking().OrderBy(x => x.Key).ToListAsync(ct);
    }
}
