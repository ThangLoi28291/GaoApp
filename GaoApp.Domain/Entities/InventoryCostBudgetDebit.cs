using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;

namespace GaoApp.Domain.Entities;

// Append-only proof of an authoritative layer interval; zero-value consumption is still a real debit.
public sealed class InventoryCostBudgetDebit : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public int InventoryCostLayerId { get; set; }
    public int InventoryTransactionId { get; set; }
    public int InventoryValuationEntryId { get; set; }
    public int InventoryCostLayerAllocationId { get; set; }
    public decimal AnchorQuantity { get; set; }
    public decimal AnchorAmount { get; set; }
    public decimal StartQuantity { get; set; }
    public decimal EndQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Amount { get; set; }
    public bool IsProvisionalResolution { get; set; }
    public decimal? ResolutionStartQuantity { get; set; }
    public decimal? ResolutionEndQuantity { get; set; }
    public int? RevaluationEntryId { get; set; }
    public InventoryCostLayer Layer { get; set; } = null!;
    public InventoryValuationEntry ValuationEntry { get; set; } = null!;
    public InventoryCostLayerAllocation Allocation { get; set; } = null!;
}
