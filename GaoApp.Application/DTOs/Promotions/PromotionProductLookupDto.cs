namespace GaoApp.Application.DTOs.Promotions;

/// <summary>
/// DTO dùng cho Select2 trong màn quản lý khuyến mãi.
/// Người dùng tìm sản phẩm theo tên / SKU / barcode,
/// hệ thống trả về ProductId, VariantId, đơn vị để lưu ngầm.
/// </summary>
public sealed class PromotionProductLookupDto
{
    public int ProductId { get; set; }

    public int VariantId { get; set; }

    public string ProductName { get; set; } = "";

    public string? VariantName { get; set; }

    public string? Sku { get; set; }

    public string? Barcode { get; set; }

    public int? BaseUnitId { get; set; }

    public string? BaseUnitName { get; set; }

    public decimal Price { get; set; }

    /// <summary>
    /// Text hiển thị trên Select2.
    /// </summary>
    public string Text { get; set; } = "";
}