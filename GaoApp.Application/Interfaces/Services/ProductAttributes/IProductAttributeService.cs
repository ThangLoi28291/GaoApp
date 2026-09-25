using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.ProductAttributes;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Services.ProductAttributes;

public interface IProductAttributeService
{
    Task<PagedResult<ProductAttributeListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<ProductAttributeListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Result<ProductAttributeEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        CreateProductAttributeRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        UpdateProductAttributeRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<List<ProductAttribute>> GetAllAsync(
        int storeId,
        CancellationToken ct = default);
}
