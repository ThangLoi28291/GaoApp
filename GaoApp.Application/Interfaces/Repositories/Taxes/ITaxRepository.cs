using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Taxes;

public interface ITaxRepository
{
    Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<(IReadOnlyList<Tax> Items, int TotalItems)> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Tax?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? excludeId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? excludeId, CancellationToken ct = default);

    Task AddAsync(Tax entity, CancellationToken ct = default);
    void Remove(Tax entity);
    Task SaveChangesAsync(CancellationToken ct = default);
}
