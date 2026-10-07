using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryInventoryCancelRequest(Guid ClientRequestId, string ExpectedVersion, string Reason);

public sealed record DeliveryInventoryCommandAck(Guid ClientRequestId, int DeliveryOrderId, string Operation,
    int AppliedRevision, string AppliedVersion, DateTimeOffset RecordedAtUtc, bool Replayed);

public sealed record DeliveryInventoryLineDto(int LineId, int VariantId, string ItemName, string BaseUnitName,
    string HeldBaseQuantityText, string DispatchedBaseQuantityText, string AcceptedReturnedBaseQuantityText,
    string StockOutsideBaseQuantityText, string? InCustodyBaseQuantityText, string? DispatchedCostText,
    string? AcceptedReturnedCostText);

public sealed record DeliveryInventoryCostFragmentDto(int FragmentId, int LineId, int JournalEntryId,
    int InventoryTransactionId, int InventoryValuationEntryId, int InventoryCostLayerId,
    int InventoryCostLayerAllocationId, int InventoryCostBudgetDebitId, string BaseQuantityText,
    string UnitCostText, string CostAmountText, string AcceptedReturnedBaseQuantityText,
    string AcceptedReturnedCostText, string CostQuality);

public sealed record DeliveryInventorySummaryDto(int StoreId, int DeliveryOrderId, int Revision, string Version,
    DateTimeOffset ServerTimeUtc, int SourceWarehouseId, string SourceWarehouseName, int SourceLegalEntityId,
    string SourceLegalEntityName, bool CanCancelBeforeHandover, bool CanViewCosts, string HeldBaseQuantityText,
    string DispatchedBaseQuantityText, string AcceptedReturnedBaseQuantityText, string StockOutsideBaseQuantityText,
    string? InCustodyBaseQuantityText, IReadOnlyList<DeliveryInventoryLineDto> Lines,
    IReadOnlyList<DeliveryInventoryCostFragmentDto> CostFragments);

// Server integration only. D06/D07 validate durable custody/outcomes before invoking these in their aggregate transaction.
public sealed record DeliveryInventoryDispatchRequest(Guid ClientRequestId, string ExpectedVersion, int ApprovedRevision);
public sealed record DeliveryInventoryReturnLine(int LineId, string BaseQuantityText);
public sealed record DeliveryInventoryReceiveRequest(Guid ClientRequestId, string ExpectedVersion,
    IReadOnlyList<DeliveryInventoryReturnLine> Lines, string Reason);
public sealed record DeliveryInventoryPostingAck(Guid ClientRequestId, int DeliveryOrderId, string Operation,
    IReadOnlyList<int> JournalEntryIds, DateTimeOffset RecordedAtUtc, bool Replayed);

// Read-only D08 adapter. Selection is server-derived delivered quantity, not an HTTP-bound sale or stock command.
public sealed record DeliveryInventoryCostSelection(int LineId, string DeliveredBaseQuantityText);
public sealed record DeliveryInventoryResidualCostDto(int LineId, string DispatchedBaseQuantityText,
    string DeliveredBaseQuantityText, string DeliveredCostText, string AcceptedReturnedBaseQuantityText,
    string AcceptedReturnedCostText, string RemainingOutsideBaseQuantityText, string RemainingOutsideCostText);
