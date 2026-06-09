namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Item lookup dùng riêng cho màn hình điều chỉnh kho.
/// Mặc dù logic tìm kiếm dùng lại từ StockDocument,
/// nhưng DTO này đặt tên đúng ngữ cảnh Adjustment để code dễ đọc, dễ bảo trì.
/// </summary>
public class InventoryAdjustmentLookupSelect2ItemDto
{
    /// <summary>
    /// Id của ProductVariant.
    /// </summary>
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Id của ProductUnitConversion nếu item này đại diện cho 1 đơn vị quy đổi cụ thể.
    /// Có thể null nếu đây là fallback base unit.
    /// </summary>
    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Đơn vị đang lookup ra.
    /// Có thể là đơn vị gốc hoặc đơn vị quy đổi.
    /// </summary>
    public int? UnitId { get; set; }

    /// <summary>
    /// Tên sản phẩm cha.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// SKU của variant.
    /// </summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Barcode tìm được tương ứng với item lookup.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Tên đơn vị đang hiển thị.
    /// </summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// Ví dụ 1 thùng = 20 bịch => Factor = 20.
    /// </summary>
    public decimal Factor { get; set; }

    /// <summary>
    /// True nếu item này là fallback từ đơn vị gốc.
    /// </summary>
    public bool IsBaseUnitFallback { get; set; }

    /// <summary>
    /// Nguồn dữ liệu:
    /// - UnitBarcode
    /// - BarcodeHistory
    /// - Keyword
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// Text hiển thị trên Select2.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public decimal? CostPrice { get; set; }
}