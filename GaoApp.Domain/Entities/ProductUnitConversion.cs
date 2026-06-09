using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Quy đổi đơn vị bán cho một ProductVariant.
/// Ví dụ:
/// - lon = 1
/// - lốc = 6 lon
/// - thùng = 24 lon
/// </summary>
[Table("ProductUnitConversion")]
public class ProductUnitConversion : BaseStoreEntity
{
    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Factor { get; set; } = 1;

    public bool IsBaseUnit { get; set; } = false;

    public bool IsDefaultForSale { get; set; } = false;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Price { get; set; }
    /// <summary>
    /// Giá sỉ riêng cho đơn vị quy đổi.
    /// 
    /// Ví dụ:
    /// - lon: giá lẻ 10.000, giá sỉ 9.500
    /// - lốc: giá lẻ 58.000, giá sỉ 55.000
    /// - thùng: giá lẻ 220.000, giá sỉ 210.000
    /// 
    /// Đây là giá ưu tiên cao nhất khi khách thuộc nhóm WHOLESALE.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    [Range(0, double.MaxValue, ErrorMessage = "Giá sỉ không hợp lệ.")]
    public decimal? WholesalePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; } = 0;

    public ProductVariant ProductVariant { get; set; } = default!;
    public Unit Unit { get; set; } = default!;

    /// <summary>
    /// Danh sách barcode từng thuộc về conversion này.
    /// Bao gồm active và inactive.
    /// </summary>
    public ICollection<ProductVariantUnitBarcode> Barcodes { get; set; }
        = new List<ProductVariantUnitBarcode>();

    /// <summary>
    /// Lịch sử đổi barcode của conversion này.
    /// </summary>
    public ICollection<ProductVariantBarcodeHistory> BarcodeHistories { get; set; }
        = new List<ProductVariantBarcodeHistory>();
}