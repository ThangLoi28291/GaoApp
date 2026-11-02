using GaoApp.Domain.Entities;

namespace GaoApp.Application.DTOs.Inventory;

// Identity-only server operations: source quantities, costs and hold exclusions are loaded and verified from persistence.
public sealed record InventoryDeliveryDispatchPosting(int DeliveryOrderId, int LineId, int ApprovedRevision, Guid RequestId);
public sealed record InventoryDeliveryReturnPosting(int DeliveryOrderId, int DispatchFragmentId,
    decimal StartQuantity, decimal Quantity, Guid RequestId);
public sealed record InventorySourcePreservingTransfer(int DocumentId, int LineId);
public sealed record InventorySourceCostMirrorPart(int SourceValuationEntryId, string? SourceReferenceSubKey,
    int SourceCostBudgetDebitId, decimal Quantity, decimal UnitCost, decimal Amount,
    bool IsProvisional = false, int? SourceProvisionalAllocationId = null, decimal? SourceStartQuantity = null);
public sealed record InventoryDeliveryPostingProof(int StoreId, int WarehouseId, int VariantId, int LegalEntityId,
    decimal Quantity, int? OwnReservationId, IReadOnlyList<InventorySourceCostMirrorPart> Parts,
    int? DeliveryOrderLineId = null, bool? StorefrontAllowNegativeInventory = null);
public sealed record InventoryTransferPostingProof(int StoreId, int DocumentId, int LineId, int SourceWarehouseId,
    int DestinationWarehouseId, int VariantId, decimal Quantity, string DocumentNo);

public sealed record InventoryBudgetResolution(InventoryCostLayer Layer, InventoryCostLayerAllocation Allocation,
    decimal StartQuantity, decimal Quantity, decimal ActualAmount, decimal ProvisionalAmount);
