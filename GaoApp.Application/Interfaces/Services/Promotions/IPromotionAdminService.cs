using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Promotions;

public interface IPromotionAdminService
{
    Task<PagedResult<PromotionListItemDto>> GetPagedAsync(
        int storeId,
        PromotionType? type,
        bool? isActive,
        string? customerPriceTier,
        string? keyword,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<Result<PromotionEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        SavePromotionRequest request,
        int? userId,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        SavePromotionRequest request,
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

    Task<Result<int>> DuplicateAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);
    Task<List<PromotionProductLookupDto>> SearchProductsForPromotionAsync(
    int storeId,
    string keyword,
    int take = 20,
    CancellationToken ct = default);

    Task<List<PromotionProductUnitLookupDto>> GetUnitsForPromotionAsync(
        int storeId,
        int variantId,
        CancellationToken ct = default);
    Task<PromotionProductLookupDto?> GetProductForPromotionAsync(
    int storeId,
    int variantId,
    CancellationToken ct = default);
}