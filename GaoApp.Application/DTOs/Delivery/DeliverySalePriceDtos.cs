using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliverySalePriceInput(int LineId, string UnitPriceText);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliverySalePriceRequest(Guid ClientRequestId, string ExpectedVersion,
    IReadOnlyList<DeliverySalePriceInput> Lines, string Reason);
public sealed record DeliverySalePriceLineDto(int LineId, string ItemName, string UnitName,
    string QuantityText, string UnitPriceText, string DiscountText, string NetText, bool CanEdit);
public sealed record DeliverySalePriceDto(bool CanEdit, string TotalText,
    IReadOnlyList<DeliverySalePriceLineDto> Lines, DateTime? ChangedAtUtc, string? ChangedBy, string? Reason);
public sealed record DeliverySalePriceAck(Guid ClientRequestId, int DeliveryOrderId, string Operation,
    int AppliedRevision, string AppliedVersion, bool Replayed);
