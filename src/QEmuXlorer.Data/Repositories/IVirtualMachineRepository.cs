using QEmuXlorer.Models.Entities;

namespace QEmuXlorer.Data.Repositories;

public interface IVirtualMachineRepository
{
    Task<IReadOnlyList<VirtualMachine>> GetAllAsync(CancellationToken ct = default);
    Task<VirtualMachine?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<VirtualMachine?> GetWithDisksAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(VirtualMachine vm, CancellationToken ct = default);
    Task UpdateAsync(VirtualMachine vm, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
