using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Inventory;

public interface IWarehouseRepository
{
    Task AddAsync(Warehouse warehouse, CancellationToken ct = default);
    Task<Warehouse?> GetByIdAsync(int id, CancellationToken ct = default);
    async Task<Warehouse?> LockByStoreAndIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        var warehouse = await GetByIdAsync(id, ct);
        return warehouse?.StoreId == storeId ? warehouse : null;
    }
    Task<Warehouse?> GetDefaultAsync(CancellationToken ct = default);
    Task<List<Warehouse>> GetAllAsync(CancellationToken ct = default);
    Task<bool> ExistsCodeAsync(string code, int? excludeId = null, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(string name, int? excludeId = null, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task ClearDefaultAsync(int? exceptWarehouseId = null, CancellationToken ct = default);
    Task<string> GenerateNextCodeAsync(CancellationToken ct = default);
}
