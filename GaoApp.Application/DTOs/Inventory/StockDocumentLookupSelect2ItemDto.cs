namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Item lookup dùng cho Select2 của màn hình StockDocument.
///
/// CHỐT KIẾN TRÚC:
/// - Mỗi item nên biết rõ ProductUnitConversionId
/// - để khi chọn từ UI, backend biết chính xác đang thao tác theo đơn vị nào
/// </summary>
public class StockDocumentLookupSelect2ItemDto
{
    /// <summary>
    /// Id của ProductVariant.
    /// </summary>
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Id của ProductUnitConversion nếu item này đại diện cho 1 đơn vị quy đổi cụ thể.
    /// Có thể null trong trường hợp fallback base unit tạm thời.
    /// </summary>
    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Id đơn vị đang lookup ra.
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
    /// Barcode gắn với item này.
    /// Nếu là fallback base unit thì có thể null.
    /// </summary>
    public string? Barcode { get; set; }

    /// <summary>
    /// Tên đơn vị đang hiển thị.
    /// </summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// </summary>
    public decimal Factor { get; set; }

    /// <summary>
    /// True nếu item này là fallback từ đơn vị gốc do dữ liệu chưa có conversion chuẩn.
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
    /// Text hiển thị cho Select2.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public decimal? Price { get; set; }
    public decimal? CostPrice { get; set; }
}