using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Persisted command journal for receiving idempotency and one-step undo.
/// It is append-only; an Undo row references the original action.
/// </summary>
public sealed class PurchaseReceivingAction
{
    public long Id { get; set; }
    public int StoreId { get; set; }
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = null!;
    public int ReceivingRevision { get; set; }
    public Guid CommandId { get; set; }
    public string? CommandPayloadHash { get; set; }
    public PurchaseReceivingActionType ActionType { get; set; }
    public int? StockDocumentLineId { get; set; }
    public StockDocumentLine? StockDocumentLine { get; set; }
    public int? StockDocumentProvisionalItemId { get; set; }
    public StockDocumentProvisionalItem? StockDocumentProvisionalItem { get; set; }
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? PurchaseOrderLineId { get; set; }
    public ReceiptAllocationKind ReceiptAllocationKind { get; set; }
    public decimal BeforeQuantity { get; set; }
    public decimal AfterQuantity { get; set; }
    public bool BeforeIsDeleted { get; set; }
    public bool AfterIsDeleted { get; set; }
    public string? BeforeProvisionalStateJson { get; set; }
    public string? AfterProvisionalStateJson { get; set; }
    public int ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public long? UndoOfActionId { get; set; }
    public PurchaseReceivingAction? UndoOfAction { get; set; }
}
