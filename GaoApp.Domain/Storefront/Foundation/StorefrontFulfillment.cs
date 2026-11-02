using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

public enum StorefrontFulfillmentStage { Picking, Picked, Dispatched, Delivered, ReturnPending, Returned, Incident }

public sealed class StorefrontFulfillmentWork : BaseStoreEntity
{
    public int? LatestEvidenceId { get; set; }
    public StorefrontFulfillmentEvidence? LatestEvidence { get; set; }
    public int StorefrontOrderId { get; set; }
    public int FulfillmentSourceId { get; set; }
    public int DeliveryOrderId { get; set; }
    public StorefrontFulfillmentStage Stage { get; set; }
    public int? CourierUserId { get; set; }
    public string? CarrierName { get; set; }
    public string? TrackingCode { get; set; }
}

// A Web12 reservation is never relabelled as Delivery11. Both identities survive.
public sealed class StorefrontReservationTransfer : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int FulfillmentSourceId { get; set; }
    public int OrderLineId { get; set; }
    public int WebReservationId { get; set; }
    public int DeliveryReservationId { get; set; }
    public decimal BaseQuantity { get; set; }
}

public sealed class StorefrontFulfillmentEvidence : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int? FulfillmentSourceId { get; set; }
    public int ActorUserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string Note { get; set; } = "";
    public string SnapshotJson { get; set; } = "{}";
}

public sealed class StorefrontCustodyIncident : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int EvidenceId { get; set; }
    public string ActualCustodySourceIdsJson { get; set; } = "[]";
    public int? ReconciledByEvidenceId { get; set; }
}
