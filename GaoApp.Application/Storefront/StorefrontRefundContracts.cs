using GaoApp.Domain.Storefront;

namespace GaoApp.Application.Storefront;

public sealed class StorefrontRefundDecisionRequest : StorefrontOperationRequest
{
    public int RefundId { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class StorefrontRefundExecutionRequest : StorefrontOperationRequest
{
    public int RefundId { get; set; }
    public StorefrontRefundPayoutSourceKind SourceKind { get; set; }
    public int SourceId { get; set; }
    public string SourceVersion { get; set; } = "";
    public int PayerLegalEntityId { get; set; }
    public int? PayerBankAccountId { get; set; }
    // Binds the account the operator actually selected before submitting a new claim.
    public string? PayerBankAccountVersion { get; set; }
    public string ExternalTransactionId { get; set; } = "";
    // Required for manually verified Treasury debits; CashOut uses its real recorded time.
    public DateTimeOffset? ExecutedAt { get; set; }
    public string EvidenceReference { get; set; } = "";
}
public sealed record StorefrontGuestRefund(int Id, StorefrontRefundState State, decimal Amount, decimal ExecutedAmount,
    string Status, string Message)
{
    public string AmountText => StorefrontCheckoutText.Money(Amount);
    public string ExecutedAmountText => StorefrontCheckoutText.Money(ExecutedAmount);
}
public sealed record StorefrontRefundExecutionRead(int Id, decimal Amount, string Fund, DateTime ExecutedAtUtc,
    string VerificationMethod, string EvidenceReference, int ConfirmedByUserId)
{
    public string AmountText => StorefrontCheckoutText.Money(Amount);
}
public sealed record StorefrontRefundRead(int Id, int OrderId, string Code, StorefrontRefundState State, decimal Amount,
    decimal ExecutedAmount, string Reason, string SourceDescription, bool CanApprove, bool CanConfirmExecution,
    string? BlockedReason, IReadOnlyList<StorefrontRefundExecutionRead> Executions)
{
    public string AmountText => StorefrontCheckoutText.Money(Amount);
    public string ExecutedAmountText => StorefrontCheckoutText.Money(ExecutedAmount);
}
public sealed record StorefrontRefundPayoutSource(StorefrontRefundPayoutSourceKind Kind, int Id, string Label, string Fund,
    decimal Amount, string Version, DateTime SourceAtUtc)
{
    public string AmountText => StorefrontCheckoutText.Money(Amount);
}
public interface IStorefrontRefundService
{
    Task<IReadOnlyList<StorefrontRefundRead>> GetAsync(int orderId, CancellationToken ct = default);
    Task<IReadOnlyList<StorefrontRefundPayoutSource>> PayoutSourcesAsync(int orderId, int refundId, CancellationToken ct = default);
    Task<StorefrontOperationAck> ApproveAsync(int orderId, StorefrontRefundDecisionRequest request, CancellationToken ct = default);
    Task<StorefrontOperationAck> RejectAsync(int orderId, StorefrontRefundDecisionRequest request, CancellationToken ct = default);
    // Claims an already recorded actual debit; never sends a payout or creates a second outflow.
    Task<StorefrontOperationAck> ConfirmExecutionAsync(int orderId, StorefrontRefundExecutionRequest request, CancellationToken ct = default);
}
