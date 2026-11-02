using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public enum BankPaymentChannel { Unknown, Pos, Storefront }
public enum BankTimePrecision { Unknown, BankDay, Timestamp }
public enum BankExternalIdentityKind { AcbRetrieve, BankNotification }
// Initial asset proof, independent of operational ReviewCode. SQL makes it immutable.
// Zero deliberately fails closed for any insertion without a trusted classification.
public enum BankReceiptAssetConfidence { Unproven, CertifiedDistinct, LegacySourceUnproven, TransactionIdentityAmbiguous }
public enum BankReceiptRoutingProvenance { Unproven, UnmatchedOnly, BoundReference }

// Global across tenants/channels. OriginStoreId is evidence, never a tenant selector.
public sealed class BankPaymentReference : BaseEntity
{
    public string AccountIdentity { get; set; } = "";
    public bool NotificationIdentityMappingVerified { get; set; }
    public string Provider { get; set; } = "ACB";
    public string Reference { get; set; } = "";
    public BankPaymentChannel Channel { get; set; }
    public int OriginStoreId { get; set; }
    public int? PosQrRequestId { get; set; }
    public int? StorefrontPaymentIntentId { get; set; }
}
public sealed class BankVerifiedReceipt : BaseEntity
{
    public BankReceiptRoutingProvenance RoutingProvenance { get; set; }
    public string AccountVersion { get; set; } = "";
    public string? NativeCollectionAuthorityProofJson { get; set; }
    public BankReceiptAssetConfidence AssetConfidence { get; set; }
    public BankExternalIdentityKind IdentityKind { get; set; }
    public bool CrossProtocolMappingVerified { get; set; }
    public string Provider { get; set; } = "ACB";
    public string AccountIdentity { get; set; } = "";
    public string ExternalTransactionId { get; set; } = "";
    public int OriginStoreId { get; set; }
    public int BankAccountId { get; set; }
    public string ProviderReference { get; set; } = "";
    public BankPaymentChannel Channel { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
    public string RawBankTime { get; set; } = "";
    public BankTimePrecision TimePrecision { get; set; }
    public DateTime? BankBusinessDate { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
    public bool NeedsReview { get; set; }
    public string? ReviewCode { get; set; }
}
public sealed class BankReceiptConflict : BaseEntity
{
    public decimal ObservedAmount { get; set; }
    public BankExternalIdentityKind ObservedIdentityKind { get; set; }
    public int BankVerifiedReceiptId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ProtectedEvidence { get; set; } = "";
    public string AttemptedReference { get; set; } = "";
    public int AttemptedStoreId { get; set; }
}
public sealed class BankReceiptConsumption : BaseEntity
{
    public int BankVerifiedReceiptId { get; set; }
    public BankPaymentChannel Channel { get; set; }
    public int OriginStoreId { get; set; }
    public int? PosPaymentId { get; set; }
    public int? StorefrontAllocationId { get; set; }
}
