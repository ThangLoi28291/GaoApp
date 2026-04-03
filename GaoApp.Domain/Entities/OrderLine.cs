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
}