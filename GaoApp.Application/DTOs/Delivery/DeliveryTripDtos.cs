using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripOrderSelection(int DeliveryOrderId, string ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripCreateDraftRequest(Guid ClientRequestId, int CourierUserId,
    IReadOnlyList<DeliveryTripOrderSelection> Orders, string? Note = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripEditDraftRequest(Guid ClientRequestId, string ExpectedTripVersion,
    int CourierUserId, IReadOnlyList<DeliveryTripOrderSelection> Orders, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripAbandonRequest(Guid ClientRequestId, string ExpectedTripVersion, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripHandoverRequest(Guid ClientRequestId, string ExpectedTripVersion,
    IReadOnlyList<DeliveryTripOrderSelection> Orders, int ConfirmedCourierUserId,
    bool PhysicalReceiptConfirmed, string? ConfirmationNote = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripDepartureRequest(Guid ClientRequestId, string ExpectedTripVersion,
    IReadOnlyList<DeliveryTripOrderSelection> Orders);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripReassignRequest(Guid ClientRequestId, string ExpectedTripVersion,
    IReadOnlyList<DeliveryTripOrderSelection> Orders, int NewCourierUserId, int ConfirmedCourierUserId,
    bool PhysicalReceiptConfirmed, string Reason, string? ConfirmationNote = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripCancellationRequest(Guid ClientRequestId, string ExpectedTripVersion,
    string ExpectedOrderVersion, string Reason);

public sealed record DeliveryTripAppliedOrder(int DeliveryOrderId, int Revision, string Version, string State);
public sealed record DeliveryTripCommandAck(Guid ClientRequestId, int TripId, string Operation,
    int AppliedRevision, string AppliedVersion, DateTimeOffset RecordedAtUtc, bool Replayed,
    IReadOnlyList<DeliveryTripAppliedOrder> Orders);
public sealed record DeliveryTripCourierOption(int UserId, string Name);
public sealed record DeliveryTripLineDto(int LineId, int VariantId, string ItemName, string BaseUnitName,
    string ApprovedBaseQuantityText, string DispatchedBaseQuantityText,
    string AcceptedReturnedBaseQuantityText, string? InCustodyBaseQuantityText);
public sealed record DeliveryCustodyContextDto(int TripId, string TripCode, string TripState,
    int CustodyEventId, string CustodyKind, int CourierUserId, string CourierName,
    int ConfirmedByUserId, string ConfirmedByName, DateTimeOffset ConfirmedAtUtc,
    int? DepartedByUserId, string? DepartedByName, DateTimeOffset? DepartedAtUtc);
public sealed record DeliveryTripOrderDto(int DeliveryOrderId, string Code, string State, int Revision,
    string Version, int Position, bool IsActive, string AssignedOrderVersion, int AssignedApprovedRevision,
    bool IsStale, int SourceWarehouseId, string SourceWarehouseName, int SourceLegalEntityId,
    string SourceLegalEntityName, string RecipientName, string RecipientPhone, string RecipientAddress,
    IReadOnlyList<DeliveryTripLineDto> Lines, DeliveryCustodyContextDto? Custody, bool CanRequestCancellation)
{
    public DeliveryOrderOutcomeDto? Outcome { get; init; }
    public bool CanReportOutcome { get; init; }
}
public sealed record DeliveryTripEligibleOrderDto(int DeliveryOrderId, string Code, int Revision,
    string Version, int ApprovedRevision, int SourceWarehouseId, string SourceWarehouseName,
    int SourceLegalEntityId, string SourceLegalEntityName, string RecipientName, string RecipientAddress,
    IReadOnlyList<DeliveryTripLineDto> Lines);
public sealed record DeliveryTripOptionsDto(int StoreId, DateTimeOffset ServerTimeUtc,
    IReadOnlyList<DeliveryTripCourierOption> Couriers, IReadOnlyList<DeliveryTripEligibleOrderDto> Orders);
public sealed record DeliveryTripCapabilities(bool CanEditDraft, bool CanAbandonDraft, bool CanHandover,
    bool CanDepart, bool CanReassignCourier, bool CanRequestCancellation);
public sealed record DeliveryTripHistoryDto(int Revision, string Action, int ActorUserId, string ActorName,
    DateTimeOffset RecordedAtUtc);
public sealed record DeliveryTripDetailDto(int StoreId, int TripId, string Code, string State, int Revision,
    string Version, DateTimeOffset ServerTimeUtc, int CourierUserId, string CourierName,
    int CreatedByUserId, string CreatedByName, DateTimeOffset CreatedAtUtc, string? Note,
    int? HandedOverByUserId, string? HandedOverByName, DateTimeOffset? HandedOverAtUtc,
    int? DepartedByUserId, string? DepartedByName, DateTimeOffset? DepartedAtUtc,
    int? AbandonedByUserId, DateTimeOffset? AbandonedAtUtc,
    IReadOnlyList<DeliveryTripOrderDto> Orders, DeliveryTripCapabilities Capabilities,
    IReadOnlyList<DeliveryTripHistoryDto> History)
{
    public DeliveryOutcomeCapabilitiesDto OutcomeCapabilities { get; init; } = new(false, false, false);
    public DeliveryTripReturnDto? ReturnToStore { get; init; }
    public bool AllOutcomesComplete { get; init; }
    public bool ReadyForReconciliation { get; init; }
    public bool IsCompleted { get; init; }
}
public sealed record DeliveryTripSummaryDto(int TripId, string Code, string State, int Revision, string Version,
    int CourierUserId, string CourierName, int ActiveOrderCount, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? HandedOverAtUtc, DateTimeOffset? DepartedAtUtc)
{
    public bool IsCompleted { get; init; }
}
public sealed record DeliveryTripPageDto(int StoreId, DateTimeOffset ServerTimeUtc,
    IReadOnlyList<DeliveryTripSummaryDto> Items, int? NextBeforeId);
