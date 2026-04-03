using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POS;


public sealed class OrderLineDto
{
    public int LineId { get; set; }
    public int VariantId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Tên biến thể để POS hiển thị thêm.
    /// Ví dụ: Màu đỏ, size L, vị cam bưởi...
    /// </summary>
    public string? ProductVariantName { get; set; }
    public string? UnitName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    /// <summary>
    /// Số lượng theo đơn vị bán
    /// </summary>
    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal LineTotal { get; set; }

    public int? SellingUnitId { get; set; }
    public string? SellingUnitName { get; set; }

    public int? BaseUnitId { get; set; }
    public string? BaseUnitName { get; set; }

    /// <summary>
    /// 1 đơn vị bán = bao nhiêu base unit
    /// </summary>
    public decimal Multiplier { get; set; }

    /// <summary>
    /// Quantity quy đổi về base unit
    /// </summary>
    public decimal BaseQuantity { get; set; }

    public string? ScannedBarcode { get; set; }

    /// <summary>
    /// 1 = UnitBarcode
    /// 2 = VariantBarcode
    /// 3 = BarcodeHistory
    /// 4 = ManualVariant
    /// </summary>
    public BarcodeLookupSourceType? BarcodeSource { get; set; }
}