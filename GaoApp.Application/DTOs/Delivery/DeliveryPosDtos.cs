using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryPosCreateRequest(Guid ClientRequestId, int SourceCartId, string ExpectedVersion,
    string ExpectedFingerprint, string RecipientName, string RecipientPhone, string RecipientAddress, string? Note);

public sealed record DeliveryCartSnapshot(int SourceCartId, string Version, string Fingerprint, int ShiftId,
    string WarehouseName, decimal QuotedTotal, string? RecipientName, string? RecipientPhone, string? RecipientAddress,
    IReadOnlyList<DeliveryCartLine> Lines);
public sealed record DeliveryCartLine(int Id, string ItemName, string UnitName, decimal Quantity, decimal LineTotal);
public sealed record DeliveryPosResult(DeliveryDetailDto Delivery, int NextCartId, string BillUrl, string LookupUrl);
