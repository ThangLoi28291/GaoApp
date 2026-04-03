using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Services.Products;

/// <summary>
/// Service lookup barcode / search sản phẩm dùng cho các nghiệp vụ:
/// - StockDocument
/// - POS
/// - InventoryAdjustment
///
/// CHỐT KIẾN TRÚC MỚI:
/// - Không còn dùng ProductVariant.Barcode
/// - Barcode duy nhất của hệ thống nằm ở ProductVariantUnitBarcode
/// </summary>
public interface IBarcodeLookupService
{
    /// <summary>
    /// Tìm sản phẩm theo barcode.
    ///
    /// Kiến trúc mới:
    /// - Chỉ match ProductVariantUnitBarcode
    /// - Không fallback về ProductVariant.Barcode nữa
    /// </summary>
    Task<BarcodeLookupResultDto?> FindAsync(string barcode, CancellationToken ct = default);

    /// <summary>
    /// Lookup Select2 đang dùng cho màn hình StockDocument.
    ///
    /// Có thể search theo:
    /// - barcode đơn vị
    /// - tên sản phẩm
    /// - SKU
    /// - từ khóa khác từ search repository
    /// </summary>
    Task<List<StockDocumentLookupSelect2ItemDto>> SearchForStockDocumentSelect2Async(
        string keyword,
        int take = 20,
        CancellationToken ct = default);

    /// <summary>
    /// Lookup Select2 dùng cho màn hình điều chỉnh kho.
    /// Logic search dùng lại từ StockDocument, chỉ map sang DTO đúng ngữ cảnh Adjustment.
    /// </summary>
    Task<List<InventoryAdjustmentLookupSelect2ItemDto>> SearchForInventoryAdjustmentSelect2Async(
        string keyword,
        int take = 20,
        CancellationToken ct = default);

    Task<BarcodeLookupResultDto> LookupAsync(string barcode, CancellationToken ct = default);
}