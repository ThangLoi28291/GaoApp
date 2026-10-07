using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryRecipientRequest(Guid ClientRequestId, string ExpectedVersion,
    string RecipientName, string RecipientPhone, string RecipientAddress, string? Note);

// Server integration contract, not an HTTP-bound request. Caller owns the surrounding transaction.
public sealed record DeliveryFoundationCreate(Guid ClientRequestId, int SourceCartId,
    string RecipientName, string RecipientPhone, string RecipientAddress, string? Note);

public sealed record DeliveryLineDto(int Id, int? SourceOrderLineId, string ItemName, string UnitName,
    string BaseUnitName, decimal OrderedQuantity, decimal BaseMultiplier, decimal UnitPrice,
    decimal Gross, decimal LineDiscount, decimal AllocatedOrderDiscount, decimal Net);

public sealed record DeliveryDetailDto(int Id, string Code, string LookupToken, string State,
    int Revision, string Version, int SourceWarehouseId, int SourceLegalEntityId, int SourceCartId,
    int CreatedTerminalId, int CreatedShiftId, int CreatedByUserId, int? CustomerId,
    string RecipientName, string RecipientPhone, string RecipientAddress, string? Note,
    decimal QuotedTotal, DateTime CreatedAtUtc, IReadOnlyList<DeliveryLineDto> Lines);

public sealed record DeliverySummaryDto(int Id, string Code, string State, int Revision,
    string RecipientName, decimal QuotedTotal, DateTime CreatedAtUtc);

public sealed record DeliveryHistoryDto(int Revision, int ActorUserId, string Action,
    DateTime RecordedAtUtc, string AggregateVersion, string SnapshotJson, string SnapshotHash);

public sealed class DeliveryFoundationException(int statusCode, string code, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
