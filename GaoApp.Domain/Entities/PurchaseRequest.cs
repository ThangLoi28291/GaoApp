using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseRequests")]
public sealed class PurchaseRequest : BaseStoreEntity, IAuditTrackedEntity
{
    [Required, StringLength(50)]
    public string RequestNumber { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Title { get; set; } = string.Empty;

    public DateTime RequestDate { get; set; } = DateTime.UtcNow;
    public DateTime? NeedByDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;
    public int RequestedByUserId { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public int? ReturnedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public int? RejectedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }
    public DateTime? ConvertedAtUtc { get; set; }
    public int? ConvertedByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }

    [StringLength(1000)]
    public string? WorkflowNote { get; set; }

    public ICollection<PurchaseRequestLine> Lines { get; set; } = new List<PurchaseRequestLine>();
    public ICollection<PurchaseRequestAction> Actions { get; set; } = new List<PurchaseRequestAction>();
    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}
