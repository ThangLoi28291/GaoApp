using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng hàng trong hóa đơn điện tử đầu vào.
/// Dữ liệu này parse từ XML.
/// </summary>
[Table("InputInvoiceDetail")]
public class InputInvoiceDetail : BaseEntity
{
    public int InputInvoiceHeadId { get; set; }
    public InputInvoiceHead InputInvoiceHead { get; set; } = default!;

    public int LineNo { get; set; }

    [StringLength(200)]
    public string? SupplierItemCode { get; set; }

    [StringLength(200)]
    public string? NormalizedSupplierItemCode { get; set; }

    [Required]
    [StringLength(500)]
    public string ItemName { get; set; } = default!;

    [StringLength(500)]
    public string? NormalizedItemName { get; set; }

    [StringLength(100)]
    public string? UnitName { get; set; }

    [StringLength(100)]
    public string? NormalizedUnitName { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineAmount { get; set; }

    [StringLength(50)]
    public string? VatRate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VatAmount { get; set; }

    /// <summary>
    /// Ghi chú parse/debug nếu cần.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    public ICollection<StockDocumentLineInputInvoiceMap> StockDocumentLineMaps { get; set; }
        = new List<StockDocumentLineInputInvoiceMap>();
}
