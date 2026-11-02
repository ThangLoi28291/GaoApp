using System.Text.Json;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Storefront;

// Operator attestation of actual bank registration, supplied by trusted server configuration only.
public sealed record StorefrontBankRegistration(bool Verified, string EvidenceReference, string MerchantId,
    string TerminalCode, string UserCode, int BankAccountId, int ReceivingLegalEntityId, string AccountIdentity,
    bool NotificationIdentityMappingVerified=false,string? NotificationIdentityMappingEvidence=null);
public interface IStorefrontBankRegistration
{
    StorefrontBankRegistration? Get(int storeId);
}
// Closed is a read-only cancelled-order projection; it is never persisted as processing state.
public enum StorefrontQrState { Queued, Creating, Pending, Paid, Review, Expired, Closed }
public sealed record StorefrontQrDto(StorefrontQrState State, string ProviderReference, decimal Amount,
    string BankName, string BeneficiaryName, string VirtualAccount, string Content, DateTime ExpiresAtUtc,
    bool QrImageReady, string Message)
{
    public string AmountText => StorefrontCheckoutText.Money(Amount);
    public string AmountValue => Amount.ToString("0",System.Globalization.CultureInfo.InvariantCulture);
}
public interface IStorefrontQrService
{
    Task<StorefrontQrDto?> GetAsync(string guestToken, CancellationToken ct = default);
    Task<byte[]?> ImageAsync(string guestToken, CancellationToken ct = default);
}
// Trusted admin/server seam only. Public endpoints cannot submit a paid/verified/account claim.
public interface IStorefrontBankProcessor
{
    Task ProcessAsync(int paymentIntentId, CancellationToken ct = default);
    Task ProcessCallbackAsync(JsonElement authenticatedPayload, CancellationToken ct = default);
}
public sealed record BankVerifiedAccount(int Id, int StoreId, string BankCode, string AccountNumber, string Version);
public sealed record BankVerifiedInput(BankVerifiedAccount Account, string ProviderReference, BankPaymentChannel Channel,
    string ExternalTransactionId, decimal Amount, string Status, string RawBankTime, string RawEvidence,
    BankExternalIdentityKind IdentityKind=BankExternalIdentityKind.AcbRetrieve,bool CrossProtocolMappingVerified=false,bool UnprovenCrossProtocolExposure=false,
    IReadOnlyList<BankCollectionAuthorityProof>? NativeCollectionAuthorities = null,
    int RefundReviewProtocolVersion = 0, string? RefundReviewObservationFingerprint = null);
public sealed record BankCollectionAuthorityProof(int Id, int BankAccountId, int ReceivingLegalEntityId, int SourceLegalEntityId, string Version);
public sealed record BankReceiptRecord(int Id, bool Conflict, bool Duplicate, BankTimePrecision TimePrecision,
    DateTime? BankBusinessDate, DateTime? OccurredAtUtc, DateTime VerifiedAtUtc);

// Private persisted snapshot. Never serialized by a customer controller.
public sealed record StorefrontBankBinding(int BankAccountId, int ReceivingLegalEntityId, string AccountIdentity,
    string BankCode, string BankName, string AccountNumber, string AccountName, string MerchantId, string BeneficiaryName,
    string BankTerminalCode, string BankUserCode, string VirtualAccountPrefix, string ProviderConfigFingerprint,
    string RegistrationEvidence, int HoldMinutes, IReadOnlyList<int> SourceLegalEntityIds, IReadOnlyList<string> AuthorityVersions,
    bool NotificationIdentityMappingVerified=false,string? NotificationIdentityMappingEvidence=null,
    StorefrontServiceCollectionProof? ServiceFee = null);
public sealed record StorefrontServiceCollectionProof(int LegalEntityId, int TaxId, string Name, decimal TaxRate,
    GaoApp.Domain.Enums.InvoiceIssuanceRoute InvoiceRoute, string ConfigurationVersion, int AuthorityId, string AuthorityVersion);

// Staff projections deliberately omit protected observations, credentials and guest-link material.
public sealed record StorefrontBankIntentRead(string Reference,decimal Amount,StorefrontQrState State,DateTime ExpiresAtUtc,string? ReviewCode)
{ public string AmountText=>StorefrontCheckoutText.Money(Amount); }
public sealed record StorefrontBankReceiptRead(string ExternalId,decimal Amount,string Status,BankTimePrecision TimePrecision,
    DateTime? BankBusinessDate,DateTime? OccurredAtUtc,bool NeedsReview,string? ReviewCode,BankReceiptAssetConfidence AssetConfidence=BankReceiptAssetConfidence.Unproven)
{ public string AmountText=>StorefrontCheckoutText.Money(Amount); }
public sealed record StorefrontBankEvidenceRead(string Code,IReadOnlyList<StorefrontBankIntentRead> Intents,
    IReadOnlyList<StorefrontBankReceiptRead> Receipts,int ProtectedObservationCount,int UnattributedCount,int ConflictCount);
public interface IStorefrontBankEvidenceReadService
{ Task<StorefrontBankEvidenceRead> GetAsync(int orderId,CancellationToken ct=default); }
