using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Suppliers;

public interface ISupplierRepository
{
    Task<(IReadOnlyList<Supplier> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<Supplier?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default);

    Task AddAsync(Supplier entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
