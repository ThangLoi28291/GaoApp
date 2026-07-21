using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

/// <summary>
/// Repository xử lý ProductVariant.
///
/// CHỐT KIẾN TRÚC MỚI:
/// - Không còn ProductVariant.Barcode dùng làm nguồn lookup chính
/// - Barcode được quản lý riêng ở ProductVariantUnitBarcode
/// - ProductVariant có thêm:
///   + ProductVariantName
///   + ProductVariantNameNormalized
/// - POS / autocomplete / order line sẽ ưu tiên hiển thị theo ProductVariantName
/// - Search keyword sẽ ưu tiên ProductVariantNameNormalized để tối ưu tốc độ
/// </summary>
public interface IProductVariantRepository
{
    // =========================
    // Load dữ liệu cho UI
    // =========================
    Task<List<AttributeWithValuesDto>> GetAttributesWithValuesAsync(int storeId, CancellationToken ct);
    Task<List<ProductVariant>> GetByProductAsync(int storeId, int productId, CancellationToken ct);

    // =========================
    // Unique check
    // =========================
    Task<bool> ExistsSkuAsync(int storeId, string sku, int? excludeVariantId, CancellationToken ct);

    // =========================
    // Save
    // =========================
    Task AddAsync(ProductVariant entity, CancellationToken ct);
    Task RemoveRangeAsync(IEnumerable<ProductVariant> entities, CancellationToken ct);
    Task RemoveMappingsByVariantAsync(int storeId, int variantId, CancellationToken ct);
    Task AddMappingsAsync(IEnumerable<ProductVariantAttributeValue> mappings, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

    Task<Dictionary<int, int>> MapValueIdsToAttributeIdsAsync(
        int storeId,
        IEnumerable<int> valueIds,
        CancellationToken ct = default);

    Task SaveVariantsAsync(
        int storeId,
        int productId,
        List<ProductVariantRowDto> variants,
        int? userId,
        CancellationToken ct = default);

    Task<bool> ToggleStatusAsync(int storeId, int variantId, int? userId, CancellationToken ct);
    Task<bool> SoftDeleteVariantAsync(int storeId, int variantId, int? userId, CancellationToken ct);

    Task<bool> SetVariantImageAsync(
        int storeId,
        int variantId,
        int? primaryProductImageId,
        int? userId,
        CancellationToken ct);

    /// <summary>
    /// Lấy variant active kèm Product và các navigation cần thiết cho lookup / POS / stock.
    /// </summary>
    Task<ProductVariant?> GetActiveWithProductAsync(int variantId, CancellationToken ct = default);

    /// <summary>
    /// Resolve variant theo barcode history.
    ///
    /// Ghi chú:
    /// - Đây là logic legacy chuyển tiếp
    /// - Hiện tại có thể vẫn hữu ích nếu bảng history cũ đang map về variant
    /// - Về lâu dài nên refactor history bám theo ProductVariantUnitBarcode hoặc conversion
    /// </summary>
    Task<int?> ResolveVariantIdByBarcodeHistoryAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Search variant cho POS / stock / lookup keyword.
    ///
    /// CHỐT:
    /// - Không lookup barcode trực tiếp tại đây
    /// - Search ưu tiên theo ProductVariantNameNormalized
    /// - Có thể fallback theo ProductVariantName / SKU / ProductName
    /// </summary>
    Task<List<ProductVariant>> SearchForPOSAsync(string keyword, int take = 20, CancellationToken ct = default);

    /// <summary>
    /// Search cho nhập kho; bao gồm sản phẩm active đang chờ hoàn thiện (IsSellable=false).
    /// </summary>
    Task<List<ProductVariant>> SearchForStockDocumentAsync(string keyword, int take = 20, CancellationToken ct = default);
}
