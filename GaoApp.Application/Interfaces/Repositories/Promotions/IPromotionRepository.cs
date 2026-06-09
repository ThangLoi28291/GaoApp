using GaoApp.Application.DTOs.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Repositories.Promotions;

public interface IPromotionRepository
{
    // =========================================================
    // POS ENGINE
    // =========================================================
    Task<List<Promotion>> GetActiveProductDiscountPromotionsAsync(
        int storeId,
        CancellationToken ct = default);

    Task<List<Promotion>> GetActiveComboPromotionsAsync(
        int storeId,
        CancellationToken ct = default);

    // =========================================================
    // ADMIN CRUD
    // =========================================================
    Task<(IReadOnlyList<Promotion> Items, int TotalItems)> GetPagedAsync(
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

    Task<Promotion?> GetByIdAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Promotion?> GetByIdForUpdateAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<bool> ExistsNameAsync(
        int storeId,
        string name,
        int? excludeId,
        CancellationToken ct = default);

    Task AddAsync(Promotion entity, CancellationToken ct = default);

    void Update(Promotion entity);

    Task SaveChangesAsync(CancellationToken ct = default);
    /// <summary>
    /// Tìm sản phẩm/biến thể để chọn vào chương trình khuyến mãi.
    /// Dùng cho Select2.
    /// </summary>
    Task<List<PromotionProductLookupDto>> SearchProductsForPromotionAsync(
        int storeId,
        string keyword,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy danh sách đơn vị bán/quy đổi của 1 biến thể.
    /// Dùng sau khi người dùng chọn sản phẩm.
    /// </summary>
    Task<List<PromotionProductUnitLookupDto>> GetUnitsForPromotionAsync(
        int storeId,
        int variantId,
        CancellationToken ct = default);
    Task<PromotionProductLookupDto?> GetProductForPromotionAsync(
    int storeId,
    int variantId,
    CancellationToken ct = default);
}