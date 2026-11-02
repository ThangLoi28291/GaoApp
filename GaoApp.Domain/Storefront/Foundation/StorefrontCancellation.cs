using GaoApp.Domain.Common;

namespace GaoApp.Domain.Storefront;

public enum StorefrontCancellationDecisionKind { Approved = 1, Rejected, SupersededByActualDelivery, SupersededByActualReturn }
public enum StorefrontRefundPayoutSourceKind { CashOut = 1, TreasuryDebit }
public enum StorefrontRefundSourceKind { QrBankReceipt = 1, CodReceipt }
public enum StorefrontRefundDecisionKind { Approved = 1, Rejected }

public sealed class StorefrontCancellationRequest : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string GuestTokenHash { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime RequestedAtUtc { get; set; }
    public bool IsImmediate { get; set; }
}
public sealed class StorefrontCancellationDecision : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int CancellationRequestId { get; set; }
    public StorefrontCancellationDecisionKind Kind { get; set; }
    public int ActorUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; }
    public bool AuthorisesPhysicalReturn { get; set; }
    public int? ActualDeliveryEvidenceId { get; set; }
    public int? ActualReturnEvidenceId { get; set; }
}
public sealed class StorefrontCancellationReviewAcknowledgement : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int CancellationDecisionId { get; set; }
    public int ActorUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime AcknowledgedAtUtc { get; set; }
}
// One durable obligation per actual source. Decisions/labels never create source money.
public sealed class StorefrontRefundFunding : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int RefundId { get; set; }
    public StorefrontRefundSourceKind Kind { get; set; }
    public int? GlobalBankReceiptId { get; set; }
    public int? CodReceiptId { get; set; }
    public int? ReceivingLegalEntityId { get; set; }
    public decimal Amount { get; set; }
    public string SourceSnapshotJson { get; set; } = "{}";
}
public sealed class StorefrontRefundDecision : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int RefundId { get; set; }
    public StorefrontRefundDecisionKind Kind { get; set; }
    public int ActorUserId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; }
}
// Produced only by trusted bank processing after complete retrieval + verified cancellation.
public sealed class StorefrontQrClosureProof : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int PaymentIntentId { get; set; }
    public int ScanCompletionId { get; set; }
    public int CancellationObservationId { get; set; }
    public string ProviderReference { get; set; } = "";
    public string AccountIdentity { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
    public DateTime VerifiedAtUtc { get; set; }
}
// Negative evidence is append-only. Operational labels cannot erase a bank correction.
public sealed class StorefrontBankReviewRestriction : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int PaymentIntentId { get; set; }
    public string ReasonCode { get; set; } = "";
    public string EvidenceKind { get; set; } = "";
    public string EvidenceFingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
}
// A start is durable before network retrieval; completion is a separate immutable fact.
public sealed class StorefrontBankScanAttempt : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int PaymentIntentId { get; set; }
    public string AccountIdentity { get; set; } = "";
    public string BindingFingerprint { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public string RequestedWindowsJson { get; set; } = "[]";
}
public sealed class StorefrontBankScanCompletion : BaseStoreEntity
{
    public int StorefrontOrderId { get; set; }
    public int PaymentIntentId { get; set; }
    public int ScanAttemptId { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
}
