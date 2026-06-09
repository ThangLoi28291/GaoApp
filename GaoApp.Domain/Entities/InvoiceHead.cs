using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("InvoiceHeads")]
public class InvoiceHead : BaseStoreEntity
{
    /// <summary>
    /// POS Order gốc tạo hóa đơn bán ra.
    /// Một Order có thể sinh một InvoiceHead.
    /// </summary>
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    [StringLength(50)]
    public string? InvoiceNumber { get; set; }

    public DateTime InvoiceDate { get; set; } = DateTime.Now;

    [StringLength(250)]
    public string? BuyerName { get; set; }

    [StringLength(50)]
    public string? BuyerTaxCode { get; set; }

    [StringLength(500)]
    public string? BuyerAddress { get; set; }

    public decimal TotalQuantity { get; set; }

    public decimal SubTotal { get; set; }

    public decimal VatAmount { get; set; }

    public decimal GrandTotal { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
    /// <summary>
    /// Invoice đã khóa thì không cho sửa/xóa/thêm dòng.
    /// Dùng sau khi xuất hóa đơn điện tử hoặc chốt kế toán.
    /// </summary>
    public bool IsLocked { get; set; }

    public DateTime? LockedAtUtc { get; set; }

    public int? LockedByUserId { get; set; }

    [StringLength(500)]
    public string? LockReason { get; set; }

    public ICollection<InvoiceDetail> Details { get; set; } = new List<InvoiceDetail>();
}