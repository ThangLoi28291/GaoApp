using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng chi tiết phiếu chuyển kho.
/// Lưu snapshot để giữ lịch sử chứng từ.
/// </summary>
[Table("StockTransferLine")]
public class StockTransferLine : BaseStoreEntity
{
    public int StockTransferDocumentId { get; set; }

    public StockTransferDocument StockTransferDocument { get; set; } = default!;

    /// <summary>
    /// Số dòng.
    /// </summary>
    public int LineNo { get; set; }

    /// <summary>
    /// Variant hàng hóa.
    /// </summary>
    public int ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = default!;

    /// <summary>
    /// Đơn vị thao tác.
    /// Có thể là đơn vị gốc hoặc đơn vị quy đổi.
    /// </summary>
    public int UnitId { get; set; }

    /// <summary>
    /// Tên đơn vị snapshot.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string UnitNameSnapshot { get; set; } = default!;

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// </summary>
    public decimal Factor { get; set; }

    /// <summary>
    /// Số lượng theo đơn vị thao tác.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Số lượng quy đổi về đơn vị gốc.
    /// BaseQuantity = Quantity * Factor
    /// </summary>
    public decimal BaseQuantity { get; set; }

    /// <summary>
    /// Tên sản phẩm snapshot.
    /// </summary>
    [Required]
    [StringLength(250)]
    public string ProductNameSnapshot { get; set; } = default!;

    /// <summary>
    /// SKU snapshot.
    /// </summary>
    [StringLength(60)]
    public string? SkuSnapshot { get; set; }

    /// <summary>
    /// Barcode snapshot.
    /// </summary>
    [StringLength(32)]
    public string? BarcodeSnapshot { get; set; }

    /// <summary>
    /// Ghi chú riêng cho dòng.
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Đơn giá vốn snapshot tại thời điểm xuất khỏi kho nguồn.
    /// 
    /// Rất quan trọng vì khi chuyển kho:
    /// - kho nguồn giảm theo cost này
    /// - kho đích nhận về thường cũng theo chính cost này
    /// </summary>
    public decimal UnitCostSnapshot { get; set; }

    /// <summary>
    /// Tổng giá trị vốn của dòng chuyển kho.
    /// </summary>
    public decimal LineCostTotal { get; set; }

    /// <summary>
    /// Đánh dấu cost của dòng chuyển kho này đang là provisional.
    /// Trường hợp hiếm nhưng vẫn nên có để mô hình nhất quán.
    /// </summary>
    public bool IsProvisionalCost { get; set; }
}