using QemuXplorer.Models.Entities;

namespace QemuXplorer.Data.Repositories;

public interface IQemuInstallationRepository
{
    Task<IReadOnlyList<QemuInstallation>> GetAllAsync(CancellationToken ct = default);
    Task<QemuInstallation?> GetDefaultAsync(CancellationToken ct = default);
    Task AddAsync(QemuInstallation installation, CancellationToken ct = default);
    Task UpdateAsync(QemuInstallation installation, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    Task SetDefaultAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string binaryDirectory, CancellationToken ct = default);
}
