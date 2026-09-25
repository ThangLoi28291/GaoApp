using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;

namespace GaoApp.Application.Interfaces.Services.Taxes;

public interface ITaxService
{
    Task<PagedResult<TaxListItemDto>> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<TaxListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<TaxEditDto?> GetForEditAsync(int storeId, int id, CancellationToken ct = default);

    Task<int> CreateAsync(int storeId, CreateTaxRequest dto, int? userId, CancellationToken ct = default);

    Task<bool> UpdateAsync(int storeId, UpdateTaxRequest dto, int? userId, CancellationToken ct = default);

    Task<bool> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default);
}
