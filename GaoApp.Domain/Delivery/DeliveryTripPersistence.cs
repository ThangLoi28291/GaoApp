using GaoApp.Domain.Common;

namespace GaoApp.Domain.Delivery;

public enum DeliveryTripState { Draft, HandedOver, Departed, Abandoned }
public enum DeliveryCustodyKind { Handover = 1, Transfer = 2 }

public sealed class DeliveryTrip : BaseStoreEntity
{
    public string Code { get; set; } = "";
    public DeliveryTripState State { get; set; }
    public int Revision { get; set; } = 1;
    public int CourierUserId { get; set; }
    public int CreatedByUserId { get; set; }
    public string? Note { get; set; }
    public int? HandedOverByUserId { get; set; }
    public DateTime? HandedOverAtUtc { get; set; }
    public int? DepartedByUserId { get; set; }
    public DateTime? DepartedAtUtc { get; set; }
    public int? AbandonedByUserId { get; set; }
    public DateTime? AbandonedAtUtc { get; set; }
}

public sealed class DeliveryTripOrder : BaseStoreEntity
{
    public int TripId { get; set; }
    public int DeliveryOrderId { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
    public string AssignedOrderVersion { get; set; } = "";
    public int AssignedApprovedRevision { get; set; }
    public int? CurrentCustodyEventId { get; set; }
}

public sealed class DeliveryCustodyEvent : BaseStoreEntity, IDeliveryImmutableRecord
{
    public Guid EventId { get; set; }
    public int TripId { get; set; }
    public int DeliveryOrderId { get; set; }
    public int TripOrderId { get; set; }
    public DeliveryCustodyKind Kind { get; set; }
    public int? PreviousCustodyEventId { get; set; }
    public int CourierUserId { get; set; }
    public int? PreviousCourierUserId { get; set; }
    public int ActorUserId { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }
    public string ConfirmationNote { get; set; } = "";
    public string? Reason { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public Guid RootClientRequestId { get; set; }
}

public sealed class DeliveryCustodyLine : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int CustodyEventId { get; set; }
    public int DeliveryOrderLineId { get; set; }
    public int DispatchJournalEntryId { get; set; }
    public int VariantId { get; set; }
    public int SourceWarehouseId { get; set; }
    public int SourceLegalEntityId { get; set; }
    public decimal ConfirmedBaseQuantity { get; set; }
    public decimal CumulativeAcceptedReturnAtConfirmation { get; set; }
}

public sealed class DeliveryTripRevision : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int TripId { get; set; }
    public int Revision { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string AggregateVersion { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public string SnapshotHash { get; set; } = "";
}

public sealed class DeliveryTripCommandReceipt : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int TripId { get; set; }
    public Guid ClientRequestId { get; set; }
    public int ActorUserId { get; set; }
    public string Operation { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string OutcomeJson { get; set; } = "";
    public DeliveryCapability RequiredCapabilities { get; set; }
}

public sealed class DeliveryTripOutboxMessage : BaseStoreEntity, IDeliveryImmutableRecord
{
    public int TripId { get; set; }
    public int Revision { get; set; }
    public Guid EventId { get; set; }
    public string Action { get; set; } = "";
    public string PayloadJson { get; set; } = "";
}
