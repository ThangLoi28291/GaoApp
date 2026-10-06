using GaoApp.Domain.Common;

namespace GaoApp.Domain.Delivery;

public interface IDeliveryImmutableRecord { }

public sealed class DeliveryOrder : BaseStoreEntity
{
    public string Code { get; set; } = "";
    public string LookupToken { get; set; } = "";
    public DeliveryState State { get; set; } = DeliveryState.Created;
    public int Revision { get; set; } = 1;
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public int SourceCartId { get; set; }
    public int CreatedTerminalId { get; set; }
    public int CreatedShiftId { get; set; }
    public int CreatedByUserId { get; set; }
    public int? CustomerId { get; set; }
    public string RecipientName { get; set; } = "";
    public string RecipientPhone { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string? Note { get; set; }
    public decimal QuotedTotal { get; set; }
    public ICollection<DeliveryOrderLine> Lines { get; set; } = new List<DeliveryOrderLine>();
}

public sealed class DeliveryOrderLine : BaseStoreEntity
{
    public int DeliveryOrderId { get; set; }
    public DeliveryOrder DeliveryOrder { get; set; } = null!;
    public int SourceCartId { get; set; }
    public int SourceOrderLineId { get; set; }
    public int VariantId { get; set; }
    public string ItemName { get; set; } = "";
    public string UnitName { get; set; } = "";
    public string BaseUnitName { get; set; } = "";
    public decimal OrderedQuantity { get; set; }
    public decimal BaseMultiplier { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Gross { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal AllocatedOrderDiscount { get; set; }
    public decimal Net { get; set; }
}

public sealed class DeliveryRevision : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int DeliveryOrderId { get; set; }
    public int Revision { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string AggregateVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
}

public enum DeliveryJournalKind { Dispatch = 1, Return = 2, CashReceipt = 3, BankConfirmation = 4, Credit = 5, Settlement = 6, Exception = 7 }

// Executors in D05/D08 own posting. D02 creates no stock or money entries.
public sealed class DeliveryJournalEntry : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int DeliveryOrderId { get; set; }
    public int? DeliveryOrderLineId { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public DeliveryJournalKind Kind { get; set; }
    public string PostingKey { get; set; } = "";
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CostAmount { get; set; }
    public decimal MoneyAmount { get; set; }
    public int? SaleOrderId { get; set; }
}

public sealed class DeliveryDispatchCostFragment : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int DeliveryOrderId { get; set; }
    public int DeliveryOrderLineId { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public int InventoryCostLayerAllocationId { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal CostAmount { get; set; }
}

public sealed class DeliveryCommandReceipt : BaseStoreEntity, IDeliveryImmutableRecord
{
    public Guid ClientRequestId { get; set; }
    public int DeliveryOrderId { get; set; }
    public int ActorUserId { get; set; }
    public string Operation { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string OutcomeJson { get; set; } = "";
}

public sealed class DeliveryOutboxMessage : BaseStoreEntity, IDeliveryImmutableRecord
{
    public Guid EventId { get; set; }
    public int DeliveryOrderId { get; set; }
    public int Revision { get; set; }
    public string Action { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}

public sealed class DeliveryOutboxReceipt : BaseStoreEntity, IDeliveryImmutableRecord
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = "";
}
