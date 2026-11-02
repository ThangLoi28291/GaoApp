using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

public sealed class StorefrontRetryableCommandException(string message) : GaoApp.Application.Common.Exceptions.ConflictAppException(message);

// No store, actor, price, warehouse or legal owner is accepted from a command.
public class StorefrontOperationRequest
{
    public Guid ClientRequestId { get; set; }
    public int ExpectedRevision { get; set; }
    public string ExpectedVersion { get; set; } = "";
    public string Note { get; set; } = "";
}
public sealed class StorefrontPickingRequest : StorefrontOperationRequest
{
    public List<StorefrontPickedLine> Lines { get; set; } = [];
}
public sealed record StorefrontPickedLine(int FulfillmentSourceId, int OrderLineId, string BaseQuantity);
public sealed class StorefrontDispatchRequest : StorefrontOperationRequest
{
    public List<StorefrontCarrierAssignment> Carriers { get; set; } = [];
}
public sealed record StorefrontCarrierAssignment(int FulfillmentSourceId, int? CourierUserId, string? CarrierName, string? TrackingCode);
public sealed class StorefrontDeliveryOutcomeRequest : StorefrontOperationRequest
{
    public int FulfillmentSourceId { get; set; }
    public bool Delivered { get; set; }
    public DateTimeOffset ResultOccurredAt { get; set; }
    public string EvidenceReference { get; set; } = "";
}
public sealed class StorefrontReceiveReturnRequest : StorefrontOperationRequest
{
    public List<StorefrontPickedLine> Lines { get; set; } = [];
}
public sealed class StorefrontIncidentRequest : StorefrontOperationRequest
{
    public List<int> CustodySourceIds { get; set; } = [];
    public string EvidenceReference { get; set; } = "";
}
public enum StorefrontIncidentResolution { AllGoodsRecoveredAtSource, AllGoodsHandedToCarrier }
public sealed class StorefrontIncidentReconcileRequest : StorefrontOperationRequest
{
    public int IncidentId { get; set; }
    public StorefrontIncidentResolution Resolution { get; set; }
    public List<StorefrontPickedLine> CountedLines { get; set; } = [];
    public List<StorefrontCarrierAssignment> Carriers { get; set; } = [];
    public string EvidenceReference { get; set; } = "";
}
public sealed record StorefrontOperationAck(int OrderId, string Operation, int Revision, string Version, bool Replayed);
public sealed record StorefrontFulfillmentLine(int OrderLineId, int DeliveryOrderLineId, string Name, string UnitName,
    string BaseQuantity, string PickedBaseQuantity, string DispatchedBaseQuantity, string ReturnedBaseQuantity, string ShortageBaseQuantity);
public sealed record StorefrontFulfillmentChild(int SourceId, int DeliveryOrderId, string WarehouseName, string LegalEntityName,
    StorefrontFulfillmentStage Stage, int? CourierUserId, string? CarrierName, string? TrackingCode,
    IReadOnlyList<StorefrontFulfillmentLine> Lines);
public sealed record StorefrontFulfillmentDetail(int Id, string Code, int Revision, string Version, StorefrontOrderPhase Phase,
    StorefrontPaymentState PaymentState, StorefrontDeliveryMethod DeliveryMethod, string RecipientName, string RecipientPhone,
    string RecipientAddress, bool HasIncident, string? PostingBlockReason, IReadOnlyList<StorefrontFulfillmentChild> Children,
    IReadOnlyList<StorefrontOperationHistory> History);
public sealed record StorefrontOperationHistory(DateTime AtUtc, string Kind, string ActorName, string Note);
public sealed record StorefrontFulfillmentQuery(int Page = 1, int PageSize = 25, StorefrontOrderPhase? Phase = null,
    string? Search = null, bool ShortageOnly = false, StorefrontDeliveryMethod? DeliveryMethod = null,
    StorefrontPaymentMethod? PaymentMethod = null);
public sealed record StorefrontFulfillmentQueue(int Page, int PageSize, int TotalCount, IReadOnlyList<StorefrontOrderInboxItem> Items);
public interface IStorefrontFulfillmentService
{
    Task<StorefrontFulfillmentQueue> ListAsync(StorefrontFulfillmentQuery query, CancellationToken ct = default);
    Task<StorefrontFulfillmentDetail> GetAsync(int id, CancellationToken ct = default);
    Task<StorefrontOperationAck> StartPreparationAsync(int id, StorefrontOperationRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RecordPickingAsync(int id, StorefrontPickingRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> DispatchAsync(int id, StorefrontDispatchRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RecordOutcomeAsync(int id, StorefrontDeliveryOutcomeRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> ReceiveReturnAsync(int id, StorefrontReceiveReturnRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RecordIncidentAsync(int id, StorefrontIncidentRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> ReconcileIncidentAsync(int id, StorefrontIncidentReconcileRequest request, CancellationToken ct = default);
}
