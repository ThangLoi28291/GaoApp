using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Custody of returned goods; no stock or cost is posted until completion.</summary>
public sealed class SalesReturnRestockFragment : BaseStoreEntity
{
    public int SalesReturnLineId { get; set; }
    public SalesReturnLine SalesReturnLine { get; set; } = default!;
    public int SourceValuationEntryId { get; set; }
    public InventoryValuationEntry SourceValuationEntry { get; set; } = default!;
    public int? AllocationReversalId { get; set; }
    public OrderLegalEntityAllocationReversal? AllocationReversal { get; set; }
    public decimal BaseQuantity { get; set; }
    public int? InventoryTransactionId { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CompletedByUserId { get; set; }
}
