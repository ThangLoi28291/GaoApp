using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseRequestActions")]
public sealed class PurchaseRequestAction : BaseStoreEntity
{
    public int PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public PurchaseRequestActionType ActionType { get; set; }
    public PurchaseRequestStatus FromStatus { get; set; }
    public PurchaseRequestStatus ToStatus { get; set; }
    public int? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(1000)]
    public string? Note { get; set; }

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
}
