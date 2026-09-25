using GaoApp.Application.Common;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Categories;

public interface ICategoryRepository
{
    Task<PagedResult<Category>> GetPagedAsync(int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<Category>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Category?> GetByIdAsync(int storeId, int id, CancellationToken ct = default);

    Task<bool> ExistsCodeAsync(int storeId, string code, int? ignoreId, CancellationToken ct = default);
    Task<bool> ExistsNameAsync(int storeId, string name, int? ignoreId, CancellationToken ct = default);

    Task<int> CreateAsync(Category entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(Category entity, CancellationToken ct = default);

    Task<bool> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default);

    Task<List<Category>> GetParentOptionsAsync(int storeId, int? excludeId, CancellationToken ct = default);
}
