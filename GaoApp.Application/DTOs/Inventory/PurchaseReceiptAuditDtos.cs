using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class PurchaseReceiptAuditEventDto
{
    public long Id { get; init; }
    public int StockDocumentId { get; init; }
    public int? StockDocumentLineId { get; init; }
    public PurchaseReceiptAuditEventType EventType { get; init; }
    public int ActorUserId { get; init; }
    public string? ActorUserName { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public string? Reason { get; init; }
    public string? Note { get; init; }
    public string ChangedFieldsJson { get; init; } = "[]";
    public string OldValuesJson { get; init; } = "{}";
    public string NewValuesJson { get; init; } = "{}";
    public string? TraceId { get; init; }
    public bool IsSuccess { get; init; }
}

public sealed class PurchaseReceiptAuditTimelineDto
{
    public int StockDocumentId { get; init; }
    public IReadOnlyList<PurchaseReceiptAuditEventDto> Events { get; init; } =
        Array.Empty<PurchaseReceiptAuditEventDto>();
}
