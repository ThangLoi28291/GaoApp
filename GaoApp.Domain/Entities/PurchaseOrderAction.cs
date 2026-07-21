using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseOrderActions")]
public sealed class PurchaseOrderAction : BaseStoreEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public PurchaseOrderActionType ActionType { get; set; }
    public PurchaseOrderStatus FromStatus { get; set; }
    public PurchaseOrderStatus ToStatus { get; set; }
    public int? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    [StringLength(1000)]
    public string? Note { get; set; }
    public int? StockDocumentId { get; set; }
    public StockDocument? StockDocument { get; set; }
}
