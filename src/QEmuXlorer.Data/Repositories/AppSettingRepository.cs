using Microsoft.EntityFrameworkCore;
using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data.Repositories;

public sealed class AppSettingRepository : IAppSettingRepository
{
    private readonly QEmuXplorerDbContext _db;

    public AppSettingRepository(QEmuXplorerDbContext db) => _db = db;

    public async Task<AppSetting?> GetAsync(string key, CancellationToken ct = default)
        => await _db.AppSettings.FindAsync([key], ct);

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default)
    {
        var setting = await _db.AppSettings.FindAsync([key], ct);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var existing = await _db.AppSettings.FindAsync([key], ct);
        if (existing is null)
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            existing.Value = value;
            _db.AppSettings.Update(existing);
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken ct = default)
        => await _db.AppSettings.OrderBy(x => x.Key).ToListAsync(ct);
}
