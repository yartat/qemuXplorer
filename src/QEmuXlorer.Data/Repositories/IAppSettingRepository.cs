using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data.Repositories;

public interface IAppSettingRepository
{
    Task<AppSetting?> GetAsync(string key, CancellationToken ct = default);
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken ct = default);
}
