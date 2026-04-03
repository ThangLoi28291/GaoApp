using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Units;

public interface IUnitRepository
{
    Task<(IReadOnlyList<Unit> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<Unit?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default);

    Task AddAsync(Unit entity, CancellationToken ct = default);
    void Remove(Unit entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
