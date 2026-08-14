using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Append-only, business-level evidence for a purchase-receipt mutation.
/// This entity intentionally does not inherit the soft-delete base types.
/// </summary>
public sealed class PurchaseReceiptAuditEvent
{
    public long Id { get; set; }
    public int StoreId { get; set; }
    public int StockDocumentId { get; set; }
    public int? StockDocumentLineId { get; set; }
    public PurchaseReceiptAuditEventType EventType { get; set; }
    public int ActorUserId { get; set; }
    public string? ActorUserName { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? Note { get; set; }
    public string ChangedFieldsJson { get; set; } = "[]";
    public string OldValuesJson { get; set; } = "{}";
    public string NewValuesJson { get; set; } = "{}";
    public string? TraceId { get; set; }
    public bool IsSuccess { get; set; }

    public Store Store { get; set; } = default!;
    public StockDocument StockDocument { get; set; } = default!;
    public StockDocumentLine? StockDocumentLine { get; set; }
}
