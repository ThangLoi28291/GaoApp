using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng chi tiết của chứng từ kho.
/// </summary>
[Table("StockDocumentLine")]
public class StockDocumentLine : BaseEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = default!;

    /// <summary>
    /// STT dòng trong phiếu: 1,2,3...
    /// </summary>
    public int LineNo { get; set; }

    /// <summary>
    /// Variant hàng hóa.
    /// </summary>
    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;

    /// <summary>
    /// Đơn vị nhập của dòng này.
    /// Ví dụ: gói, lốc, thùng...
    /// </summary>
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    /// <summary>
    /// Snapshot tên đơn vị tại thời điểm tạo phiếu.
    /// </summary>
    [StringLength(100)]
    public string? UnitNameSnapshot { get; set; }

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// Ví dụ thùng = 30 gói => Factor = 30.
    /// </summary>
    [Column(TypeName = "decimal(18,4)")]
    public decimal Factor { get; set; } = 1;

    /// <summary>
    /// Số lượng theo đơn vị nhập.
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Số lượng quy đổi về đơn vị gốc.
    /// BaseQuantity = Quantity * Factor
    /// </summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal BaseQuantity { get; set; }

    /// <summary>
    /// Đơn giá nhập theo đơn vị nhập.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitCost { get; set; }

    /// <summary>
    /// Thành tiền dòng = Quantity * UnitCost.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    /// <summary>
    /// Snapshot tên hàng để sau này đổi tên sản phẩm
    /// vẫn xem lại chứng từ cũ đúng ngữ cảnh tại thời điểm nhập.
    /// </summary>
    [Required]
    [StringLength(250)]
    public string ProductNameSnapshot { get; set; } = default!;

    [StringLength(100)]
    public string? SkuSnapshot { get; set; }

    [StringLength(100)]
    public string? BarcodeSnapshot { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
    /// <summary>
    /// Map dòng nhập kho này với dòng hóa đơn XML.
    /// </summary>
    public ICollection<StockDocumentLineInputInvoiceMap> InputInvoiceMaps { get; set; }
        = new List<StockDocumentLineInputInvoiceMap>();


}