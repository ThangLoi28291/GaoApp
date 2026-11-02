using System.Text.Json.Serialization;

namespace GaoApp.Application.DTOs.Delivery;

public sealed class DeliveryWorkspaceQuery
{
    public string Stage { get; set; } = "picking";
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
}

public sealed record DeliveryWorkspaceCounts(int Picking, int Ready, int Delivering, int Completed, int Attention);
public sealed record DeliveryWorkspaceRow(int Id, string Code, string State, int Revision, string Version,
    string RecipientName, string RecipientPhone, string RecipientAddress, string TotalText, DateTime CreatedAtUtc,
    int? TripId, string? TripCode, string? CourierName, string? PickerName, bool NeedsAttention);
public sealed record DeliveryWorkspacePage(int StoreId, DeliveryWorkspaceCounts Counts, int Page, int Pages, int Total,
    IReadOnlyList<DeliveryWorkspaceRow> Items, bool CanDispatch);
public sealed record DeliveryWorkspaceContext(int DeliveryOrderId, int? TripId, string? IssueReason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryDispatchRequest(Guid ClientRequestId, int CourierUserId,
    IReadOnlyList<DeliveryTripOrderSelection> Orders, bool PhysicalReceiptConfirmed, string? Note = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeliveryCounterIssueRequest(Guid ClientRequestId, string ExpectedTripVersion,
    string ExpectedOrderVersion, string Reason);
