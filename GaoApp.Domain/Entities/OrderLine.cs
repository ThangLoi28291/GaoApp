using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

public class OrderLine : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public int ProductId { get; set; }

    public int VariantId { get; set; }
    public ProductVariant? Variant { get; set; }

    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Tên đơn vị hiển thị trên dòng hàng.
    /// Thường sẽ chính là SellingUnitName.
    /// </summary>
    public string? UnitName { get; set; }

    public string? Sku { get; set; }
    public string? Barcode { get; set; }

    /// <summary>
    /// Số lượng theo đơn vị bán hiện tại.
    /// Ví dụ:
    /// - 2 thùng
    /// - 3 lốc
    /// - 5 kg
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Đơn giá vốn snapshot tại thời điểm finalize đơn.
    /// Trước khi finalize có thể để null.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? UnitCostSnapshot { get; set; }

    /// <summary>
    /// Tổng giá vốn snapshot của dòng bán.
    /// Thường = BaseQuantity * UnitCostSnapshot.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? LineCostTotal { get; set; }

    /// <summary>
    /// Lãi gộp snapshot của dòng bán.
    /// Thường = LineTotal - LineCostTotal.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? GrossProfit { get; set; }

    /// <summary>
    /// Dòng bán này có đang dùng giá vốn tạm hay không.
    /// Dùng để đánh dấu line cần manager review / revaluation.
    /// </summary>
    public bool IsProvisionalCost { get; set; }

    /// <summary>
    /// Nếu line đang dùng provisional cost thì có thể lưu riêng cost tạm.
    /// Không bắt buộc, nhưng hữu ích cho audit/revaluation.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? ProvisionalUnitCost { get; set; }

    /// <summary>
    /// Ghi chú nguồn cost snapshot/provisional để debug và audit.
    /// </summary>
    public string? CostSnapshotNote { get; set; }

    public int? SellingUnitId { get; set; }
    /// <summary>
    /// Snapshot ProductUnitConversion thực tế
    /// dùng khi bán hàng.
    /// Đây mới là khóa chính xác để repricing,
    /// promotion và audit.
    /// </summary>
    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Tên đơn vị bán snapshot tại thời điểm bán.
    /// </summary>
    public string? SellingUnitName { get; set; }

    /// <summary>
    /// Đơn vị gốc tồn kho của Product.
    /// Ví dụ: gói / chai / kg
    /// </summary>
    public int? BaseUnitId { get; set; }

    /// <summary>
    /// Tên đơn vị gốc snapshot tại thời điểm bán.
    /// </summary>
    public string? BaseUnitName { get; set; }

    /// <summary>
    /// 1 đơn vị bán = bao nhiêu đơn vị gốc.
    /// Ví dụ:
    /// - 1 thùng = 24 gói => Multiplier = 24
    /// - 1 lốc = 6 lon => Multiplier = 6
    /// - bán đúng base unit => Multiplier = 1
    /// </summary>
    public decimal Multiplier { get; set; } = 1m;

    /// <summary>
    /// Quantity quy đổi về base unit.
    /// Dùng cho inventory ledger.
    /// </summary>
    public decimal BaseQuantity { get; set; }

    /// <summary>
    /// Barcode thực tế cashier quét.
    /// </summary>
    public string? ScannedBarcode { get; set; }

    /// <summary>
    /// Nguồn barcode:
    /// 1 = ProductVariantUnitBarcode
    /// 2 = ProductVariant.Barcode
    /// 3 = BarcodeHistory
    /// 4 = ManualVariant
    /// </summary>
    public BarcodeLookupSourceType? BarcodeSource { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineDiscount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal OriginalUnitPrice { get; set; }

    /// <summary>Retail price of one base unit when the server last priced this line.</summary>
    public decimal? RewardBaseUnitPrice { get; set; }

    /// <summary>Amount actually eligible at checkout. Null denotes a legacy/unfinished line.</summary>
    public decimal? RewardableAmountSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PromotionDiscount { get; set; }

    public int? PromotionId { get; set; }

    public string? PromotionName { get; set; }
    public int? ComboPromotionId { get; set; }

    public string? ComboPromotionName { get; set; }

    public string? ComboPromotionNote { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ComboAllocatedDiscount { get; set; }
    /// <summary>
    /// Loại khuyến mãi sản phẩm đang áp trên dòng.
    /// 1 = ProductDiscount
    /// 2 = ComboFixedPrice, mixed-quantity group allocated to this line
    /// 3 = BuyXGetY
    /// Null = không có khuyến mãi dòng.
    /// </summary>
    public PromotionType? PromotionType { get; set; }

    /// <summary>
    /// Mua bao nhiêu để được tặng.
    /// Ví dụ mua 10 tặng 1 => 10.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal PromotionBuyQuantity { get; set; }

    /// <summary>
    /// Số lượng được tặng thực tế trên dòng.
    /// Ví dụ SL 11, mua 10 tặng 1 => 1.
    /// SL 22 => 2.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal PromotionGiftQuantity { get; set; }
    // =========================================================
    // BUY X GET Y - DÒNG HÀNG TẶNG
    // =========================================================

    /// <summary>
    /// Đánh dấu đây là dòng hàng tặng do khuyến mãi sinh ra.
    /// 
    /// Lưu ý:
    /// - Dòng hàng mua bình thường: false
    /// - Dòng hàng tặng: true
    /// - Dòng tặng luôn UnitPrice = 0, LineTotal = 0
    /// </summary>
    public bool IsPromotionGift { get; set; }

    /// <summary>
    /// PromotionId của chương trình sinh ra dòng quà.
    /// Không dùng chung với PromotionId của giảm giá sản phẩm để tránh lẫn Type 1.
    /// </summary>
    public int? GiftPromotionId { get; set; }

    /// <summary>
    /// OrderLineId của dòng mua gốc sinh ra dòng tặng.
    /// Ví dụ:
    /// - Dòng mua: Xoài sấy SL 11
    /// - Dòng tặng: Xoài sấy SL 1
    /// => GiftSourceLineId = Id của dòng mua.
    /// </summary>
    public int? GiftSourceLineId { get; set; }

    /// <summary>
    /// Tên chương trình khuyến mãi dùng để hiển thị POS / bill.
    /// Ví dụ: Mua 10 tặng 1.
    /// </summary>
    public string? GiftPromotionName { get; set; }

    /// <summary>
    /// Ghi chú rõ ràng cho dòng hàng tặng.
    /// Ví dụ: Hàng tặng từ CTKM: Mua 10 tặng 1.
    /// </summary>
    public string? GiftPromotionNote { get; set; }

}
