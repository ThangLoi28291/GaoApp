using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

// Guest capability is supplied only in the protected POST body. No client store/actor IDs.
public sealed class StorefrontGuestCancellationRequest : StorefrontOperationRequest
{
    public string Token { get; set; } = "";
    public string Reason { get; set; } = "";
}
public sealed class StorefrontCancellationDecisionRequest : StorefrontOperationRequest
{
    public int CancellationRequestId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed record StorefrontCancellationHistory(string Status, DateTime AtUtc, string Reason);
public sealed record StorefrontGuestCancellationView(string Code, StorefrontOrderPhase Phase, int Revision, string Version,
    bool CanSelfCancel, bool CanRequestCancellation, bool CancellationPending, bool ReturnAuthorised,
    bool HasDeliveredGoods, bool HasCustodyIncident, string Message, IReadOnlyList<StorefrontCancellationHistory> History,
    IReadOnlyList<StorefrontGuestRefund> Refunds)
{
    public StorefrontRefundState RefundState { get; init; }
}
public sealed record StorefrontCancellationRead(int OrderId, string Code, StorefrontOrderPhase Phase, int Revision, string Version,
    int? PendingRequestId, string? RequestedReason, bool ReturnAuthorised, bool HasPhysicalDispatch, bool HasDeliveredGoods,
    bool HasCustodyIncident, bool CanApprove, bool CanReject, string? BlockedReason, IReadOnlyList<StorefrontCancellationHistory> History,
    bool NeedsDeliveryConflictReview = false, bool CanAcknowledgeDeliveryConflict = false, int? DeliveryConflictRequestId = null);
public enum StorefrontOrderControlFilter { PendingCancellation, RefundOutstanding, DeliveryConflictReview, All }
public sealed class StorefrontOrderControlQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public StorefrontOrderControlFilter Filter { get; set; } = StorefrontOrderControlFilter.PendingCancellation;
}
public sealed record StorefrontOrderControlRow(int OrderId, string Code, string RecipientName, DateTime CreatedAtUtc,
    StorefrontOrderPhase Phase, StorefrontRefundState RefundState, bool CancellationPending, bool NeedsDeliveryConflictReview,
    decimal RefundAmount, decimal ExecutedRefundAmount, bool HasUnreconciledRefundSource)
{
    public string RefundAmountText => StorefrontCheckoutText.Money(RefundAmount);
    public string ExecutedRefundAmountText => StorefrontCheckoutText.Money(ExecutedRefundAmount);
}
public sealed record StorefrontOrderControlPage(int Page, int PageSize, int TotalCount, IReadOnlyList<StorefrontOrderControlRow> Items);
public interface IStorefrontCancellationService
{
    Task<StorefrontOrderControlPage> ListAsync(StorefrontOrderControlQuery query, CancellationToken ct = default);
    Task<StorefrontGuestCancellationView?> GetGuestAsync(string token, CancellationToken ct = default);
    // The server chooses immediate cancellation or a review request from current physical facts.
    Task<StorefrontOperationAck> CancelGuestAsync(StorefrontGuestCancellationRequest request, CancellationToken ct = default);
    Task<StorefrontCancellationRead> GetAsync(int orderId, CancellationToken ct = default);
    Task<StorefrontOperationAck> ApproveAsync(int orderId, StorefrontCancellationDecisionRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RejectAsync(int orderId, StorefrontCancellationDecisionRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> AcknowledgeDeliveryConflictAsync(int orderId, StorefrontCancellationDecisionRequest request, CancellationToken ct = default);
}
