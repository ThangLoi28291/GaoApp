using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("InvoiceDetails")]
public class InvoiceDetail : BaseStoreEntity
{
    public int InvoiceHeadId { get; set; }
    public InvoiceHead InvoiceHead { get; set; } = default!;

    /// <summary>
    /// Có nếu dòng này sinh từ POS OrderLine.
    /// Null nếu dòng thêm tay.
    /// </summary>
    public int? OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    /// <summary>
    /// Có nếu dòng hóa đơn gắn được với biến thể sản phẩm.
    /// Null nếu thêm tay tự do.
    /// </summary>
    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }

    public InvoiceDetailSourceType SourceType { get; set; }

    [Required]
    [StringLength(250)]
    public string ItemName { get; set; } = default!;

    [StringLength(100)]
    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Amount { get; set; }

    public decimal VatRate { get; set; }

    public decimal VatAmount { get; set; }

    public decimal TotalAmount { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}