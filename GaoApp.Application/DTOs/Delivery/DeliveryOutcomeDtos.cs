using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryOutcomeLineRequest(int LineId, string DeliveredBaseQuantityText,
    string ReportedReturnBaseQuantityText);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryOutcomeRequest(Guid ClientRequestId, string ExpectedTripVersion,
    string ExpectedOrderVersion, int ReportedCourierUserId, string ReportSource, string Outcome,
    string? Reason, string ReportedCashText, string ReportedTransferText,
    DateTimeOffset? CustomerReceivedAtUtc, IReadOnlyList<DeliveryOutcomeLineRequest> Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripReturnRequest(Guid ClientRequestId, string ExpectedTripVersion,
    int ConfirmedCourierUserId, bool PhysicalReturnConfirmed, string? ConfirmationNote = null);

public sealed record DeliveryOutcomeLineDto(int LineId, string DeliveredBaseQuantityText,
    string ReportedReturnBaseQuantityText, string AcceptedReturnedAtReportBaseQuantityText,
    string PendingReturnBaseQuantityText);
public sealed record DeliveryOrderOutcomeDto(int OrderRevision, string OrderVersion, int TripId,
    int DeliveryOrderId, int CustodyEventId, int ActorUserId, string ActorName, int CourierUserId,
    string CourierName, string ReportSource, string Outcome, string? Reason, string ReportedCashText,
    string ReportedTransferText, DateTimeOffset RecordedAtUtc, DateTimeOffset? CustomerReceivedAtUtc,
    IReadOnlyList<DeliveryOutcomeLineDto> Lines)
{
    public bool IsCurrentValid { get; init; } = true;
}
public sealed record DeliveryTripReturnDto(int TripRevision, int ActorUserId, string ActorName,
    int CourierUserId, string CourierName, DateTimeOffset RecordedAtUtc, string? ConfirmationNote);
public sealed record DeliveryOutcomeCapabilitiesDto(bool CanReportOwn, bool CanReportOnBehalf,
    bool CanRecordReturn);

// Typed durable envelopes: source quantities and identities are validated independently of generic JSON checks.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryOutcomeLineSnapshot(int LineId, int VariantId, int DispatchJournalEntryId,
    string DispatchedBaseQuantityText, string AcceptedReturnedAtReportBaseQuantityText,
    string DeliveredBaseQuantityText, string ReportedReturnBaseQuantityText);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryOutcomeSnapshot(string Schema, int StoreId, int TripId, int DeliveryOrderId,
    int TripOrderId, int CustodyEventId, int SourceWarehouseId, int SourceLegalEntityId,
    int OrderRevision, string OrderVersion, string OrderState, Guid RootClientRequestId, int ActorUserId, int CourierUserId,
    string ReportSource, string Outcome, string? Reason, string ReportedCashText, string ReportedTransferText,
    DateTimeOffset DepartedAtUtc, DateTimeOffset RecordedAtUtc, DateTimeOffset? CustomerReceivedAtUtc,
    IReadOnlyList<DeliveryOutcomeLineSnapshot> Lines);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryTripReturnSnapshot(string Schema, int StoreId, int TripId, int TripRevision,
    Guid RootClientRequestId, int ActorUserId, int CourierUserId, DateTimeOffset DepartedAtUtc,
    DateTimeOffset RecordedAtUtc, bool PhysicalReturnConfirmed, string? ConfirmationNote);
