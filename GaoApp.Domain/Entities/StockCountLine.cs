using GaoApp.Domain.Common;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng kiểm kê kho.
/// 
/// Lưu ý:
/// - SystemQtyBase: tồn hệ thống tại thời điểm lên dòng kiểm kê
/// - CountedQty: số người dùng đếm thực tế theo đơn vị đang chọn
/// - CountedQtyBase: quy đổi về đơn vị gốc
/// - DifferenceQtyBase = CountedQtyBase - SystemQtyBase
/// </summary>
[Table("StockCountLine")]
public class StockCountLine : BaseStoreEntity
{
    /// <summary>
    /// Phiếu kiểm kê cha.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "StockCountDocumentId không hợp lệ.")]
    public int StockCountDocumentId { get; set; }

    public StockCountDocument StockCountDocument { get; set; } = null!;

    /// <summary>
    /// Số thứ tự dòng trong phiếu.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int LineNo { get; set; }

    /// <summary>
    /// Variant được kiểm kê.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "ProductVariantId không hợp lệ.")]
    public int ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>
    /// Đơn vị người dùng đang dùng để nhập số lượng đếm.
    /// Có thể là đơn vị gốc hoặc đơn vị quy đổi.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "UnitId không hợp lệ.")]
    public int UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    /// <summary>
    /// Snapshot tên đơn vị tại thời điểm kiểm kê.
    /// </summary>
    [StringLength(100)]
    public string? UnitNameSnapshot { get; set; }

    /// <summary>
    /// Hệ số quy đổi từ đơn vị nhập về đơn vị gốc.
    /// Ví dụ:
    /// - chai = 1
    /// - lốc = 6
    /// - thùng = 24
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal Factor { get; set; } = 1m;

    /// <summary>
    /// Tồn hệ thống theo đơn vị gốc tại thời điểm tạo / refresh dòng.
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal SystemQtyBase { get; set; }

    /// <summary>
    /// Số lượng đếm thực tế người dùng nhập theo đơn vị đang chọn.
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal CountedQty { get; set; }

    /// <summary>
    /// Số lượng đếm thực tế quy đổi về đơn vị gốc.
    /// CountedQtyBase = CountedQty * Factor
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal CountedQtyBase { get; set; }

    /// <summary>
    /// Chênh lệch sau kiểm kê theo đơn vị gốc.
    /// DifferenceQtyBase = CountedQtyBase - SystemQtyBase
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal DifferenceQtyBase { get; set; }

    /// <summary>
    /// Snapshot tên sản phẩm tại thời điểm kiểm kê.
    /// </summary>
    [Required]
    [StringLength(250)]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    /// <summary>
    /// Snapshot SKU tại thời điểm kiểm kê.
    /// </summary>
    [StringLength(100)]
    public string? SkuSnapshot { get; set; }

    /// <summary>
    /// Snapshot barcode tại thời điểm kiểm kê.
    /// Có thể là barcode variant hoặc barcode của đơn vị nếu muốn lưu riêng về sau.
    /// </summary>
    [StringLength(100)]
    public string? BarcodeSnapshot { get; set; }

    /// <summary>
    /// Ghi chú riêng từng dòng kiểm kê.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Đơn giá vốn snapshot dùng khi xác nhận chênh lệch kiểm kê.
    /// 
    /// Ví dụ:
    /// - count gain: tăng kho theo cost này
    /// - count loss: giảm kho theo cost này
    /// </summary>
    public decimal UnitCostSnapshot { get; set; }

    /// <summary>
    /// Giá trị tác động của dòng kiểm kê.
    /// Có thể dương hoặc âm tùy quy ước service xác nhận.
    /// </summary>
    public decimal LineCostTotal { get; set; }

    /// <summary>
    /// Dòng kiểm kê này có đang dùng cost tạm hay không.
    /// </summary>
    public bool IsProvisionalCost { get; set; }

 
}