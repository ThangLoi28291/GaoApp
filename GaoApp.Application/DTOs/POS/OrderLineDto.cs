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
    /// Ảnh đầy đủ để preview / popup xem lớn.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Ảnh thumbnail nhỏ dùng cho giỏ và autocomplete.
    /// Nếu chưa có resize riêng thì có thể tạm dùng cùng ImageUrl.
    /// </summary>
    public string? ImageThumbUrl { get; set; }

    /// <summary>
    /// Alt text cho ảnh.
    /// </summary>
    public string? ImageAlt { get; set; }

    /// <summary>
    /// Giúp UI render nhanh, đỡ check null nhiều nơi.
    /// </summary>
    public bool HasImage { get; set; }

    /// <summary>
    /// Số lượng theo đơn vị bán
    /// </summary>
    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }
    public List<OrderLineUnitPriceDto> UnitPrices { get; set; } = new();
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
    public decimal OriginalUnitPrice { get; set; }

    public decimal PromotionDiscount { get; set; }

    public string? PromotionName { get; set; }

    /// <summary>
    /// Quantity quy đổi về base unit
    /// </summary>
    public decimal BaseQuantity { get; set; }

    public string? ScannedBarcode { get; set; }
    public int? ComboPromotionId { get; set; }

    public string? ComboPromotionName { get; set; }

    public string? ComboPromotionNote { get; set; }

    public decimal ComboAllocatedDiscount { get; set; }
    public PromotionType? PromotionType { get; set; }

    public decimal PromotionBuyQuantity { get; set; }

    public decimal PromotionGiftQuantity { get; set; }
    // =========================================================
    // BUY X GET Y - DÒNG HÀNG TẶNG
    // =========================================================

    public bool IsPromotionGift { get; set; }

    public int? GiftPromotionId { get; set; }

    public int? GiftSourceLineId { get; set; }

    public string? GiftPromotionName { get; set; }

    public string? GiftPromotionNote { get; set; }

    /// <summary>
    /// 1 = UnitBarcode
    /// 2 = VariantBarcode
    /// 3 = BarcodeHistory
    /// 4 = ManualVariant
    /// </summary>
    public BarcodeLookupSourceType? BarcodeSource { get; set; }
}
public class OrderLineUnitPriceDto
{
    public string UnitName { get; set; } = "";
    public decimal Factor { get; set; }

    public decimal? RetailPrice { get; set; }
    public decimal? WholesalePrice { get; set; }

    public decimal EffectivePrice { get; set; }

    public bool IsBaseUnit { get; set; }
    public bool IsCurrentUnit { get; set; }
    /// <summary>
    /// Dòng giá đang thực sự được áp dụng cho OrderLine hiện tại.
    /// Ví dụ:
    /// - Bán 48 cái nhưng đang tính theo giá thùng
    /// => IsEffectivePriceUnit = true ở dòng Thùng.
    /// </summary>
    public bool IsEffectivePriceUnit { get; set; }

}