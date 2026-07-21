using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchasePayables")]
public sealed class PurchasePayable : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = null!;
    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public PurchasePayableType Type { get; set; }
    [Required, StringLength(150)]
    public string SourceKey { get; set; } = string.Empty;
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    [StringLength(250)]
    public string? PayeeName { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    public PurchasePayableStatus Status { get; set; } = PurchasePayableStatus.Outstanding;
    public DateTime RecognizedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
    [StringLength(1000)]
    public string? Note { get; set; }
}
