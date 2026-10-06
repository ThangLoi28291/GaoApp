using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class OperatingExpense : BaseStoreEntity, IAuditTrackedEntity
{
    public Guid ClientRequestId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "other";
    public decimal Amount { get; set; }
    public DateTime RecognitionFrom { get; set; }
    public DateTime RecognitionTo { get; set; }
    public string Status { get; set; } = "draft";
    public bool IsPaid { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? ReceiptReference { get; set; }
    public string? Note { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedBy { get; set; }
    public string? VoidReason { get; set; }
}
