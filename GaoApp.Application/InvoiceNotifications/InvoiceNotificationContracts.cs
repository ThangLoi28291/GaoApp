using GaoApp.Domain.Entities;

namespace GaoApp.Application.InvoiceNotifications;

public sealed class InvoiceNotificationOptions
{
    public const string SectionName = "InvoiceNotifications";
    public bool Enabled { get; set; }
    public string PilotPhone { get; set; } = "0398946174";
    public int LeaseSeconds { get; set; } = 120;
}
public sealed record InvoiceRecipientBindingProof(int StoreId,int CustomerId,string ConnectionId,string ConnectionBinding,
    Guid SenderProfileId,Guid ConversationId,long IncomingSequence,string NativePeer,string Phone,int ActorUserId,string Method,string EvidenceReference);
public sealed record InvoiceRecipientBindingSnapshot(Guid Id,int StoreId,int CustomerId,string ConnectionId,string ConnectionBinding,
    Guid SenderProfileId,Guid ConversationId,long IncomingSequence,string NativePeer,string Phone,int Version,DateTime VerifiedAtUtc,DateTime? RevokedAtUtc);
public sealed record InvoiceDocumentLine(int Id,string Name,string? Unit,decimal Quantity,decimal UnitPrice,decimal LineDiscount,decimal LineTotal)
{
    public decimal PromotionDiscount {get;init;}
    public decimal ComboAllocatedDiscount {get;init;}
    public string? PromotionType {get;init;}
    public bool IsPromotionGift {get;init;}
}
public sealed record InvoiceDocumentPayment(string Method,decimal Amount,string? Reference);
public sealed record InvoiceDocumentArtifact(string StoragePath,string FileName,string ContentType,long ByteLength,string Sha256);
public sealed record InvoiceDocumentSnapshot
{
    public int SchemaVersion {get;init;}=1;
    public int StoreId {get;init;}
    public int? CustomerId {get;init;}
    public string SourceType {get;init;}="";
    public int SourceId {get;init;}
    public string SourceVersion {get;init;}="";
    public string Kind {get;init;}="";
    public int? OrderId {get;init;}
    public string? OrderCode {get;init;}
    public string? LegalEntityName {get;init;}
    public int? InvoiceId {get;init;}
    public int? OriginalInvoiceId {get;init;}
    public int? LegalEntityId {get;init;}
    public string? OriginalInvoiceCode {get;init;}
    public string? CorrectionType {get;init;}
    public string Code {get;init;}="";
    public string Status {get;init;}="";
    public string PaymentStatus {get;init;}="";
    public string StoreName {get;init;}="";
    public string? CustomerName {get;init;}
    public string? CustomerPhone {get;init;}
    public DateTime? FinalizedAtUtc {get;init;}
    public DateTime? IssuedAtUtc {get;init;}
    public decimal Subtotal {get;init;}
    public decimal LineDiscountTotal {get;init;}
    public decimal DiscountTotal {get;init;}
    public decimal OrderDiscount {get;init;}
    public decimal PromotionDiscountTotal {get;init;}
    public decimal ComboDiscountTotal {get;init;}
    public decimal VoucherDiscountTotal {get;init;}
    public decimal GrandTotal {get;init;}
    public decimal DepositAmount {get;init;}
    public decimal PaidTotal {get;init;}
    public decimal ChangeDue {get;init;}
    public decimal BalanceDue {get;init;}
    public decimal? CurrentPoints {get;init;}
    public IReadOnlyList<InvoiceDocumentLine> Lines {get;init;}=[];
    public IReadOnlyList<InvoiceDocumentPayment> Payments {get;init;}=[];
    public InvoiceDocumentArtifact? Artifact {get;init;}
    public string? UnsupportedReason {get;init;}
}
public sealed record InvoicePreparedPart(string Kind,string? Text,string? FileName,string? ContentType,long? ByteLength,string? Sha256,string? StorageKey);
public sealed record InvoicePreparedBundle(IReadOnlyList<InvoicePreparedPart> Parts,string Fingerprint);
public interface IInvoiceNotificationBundleBuilder
{
    Task<InvoicePreparedBundle> BuildAsync(InvoiceDocumentSnapshot snapshot,CancellationToken ct);
}
public sealed record InvoiceNotificationWorkItem(Guid Id,Guid LeaseToken,int StoreId,int CustomerId,string SourceType,int SourceId,string SourceVersion,
    string Kind,Guid RequestId,string IdempotencyKey,string State,DateTime CreatedAtUtc,InvoiceRecipientBindingSnapshot Binding,
    InvoiceDocumentSnapshot Snapshot,InvoicePreparedBundle? Prepared,Guid? ZaloJobId,int Attempts);
public sealed record InvoiceNotificationListItem(Guid Id,int StoreId,int? CustomerId,string SourceType,int SourceId,string SourceVersion,string Kind,
    string Code,string State,string? Detail,DateTime CreatedAtUtc,DateTime? UpdatedAtUtc,Guid? ZaloJobId,int Attempts);
public sealed record InvoiceNotificationDetail(InvoiceNotificationListItem Item,InvoiceRecipientBindingSnapshot? Binding,
    InvoiceDocumentSnapshot Snapshot,InvoicePreparedBundle? Prepared,string? PartsOutcomeJson);
public interface IInvoiceNotificationCapture
{
    // Stages only. The owning business transaction must save and commit this change with its source.
    Task CaptureFinalizedOrderAsync(Order order,decimal? currentPoints,CancellationToken ct);
}
public sealed record InvoiceNotificationEnqueueCommand(int StoreId,int CustomerId,string SourceType,int SourceId,string Kind,Guid RequestId,int ActorUserId,Guid ExpectedBindingId,int ExpectedBindingVersion,string ExpectedSourceVersion);
public sealed record InvoiceNotificationSourcePreview(int StoreId,int CustomerId,string SourceType,int SourceId,string Kind,string SourceVersion,string Code,string Status,decimal GrandTotal,string? UnsupportedReason,int? LegalEntityId=null,int? OriginalInvoiceId=null,string? OriginalInvoiceCode=null);
public interface IInvoiceNotificationStore
{
    Task<InvoiceRecipientBindingSnapshot> VerifyBindingAsync(InvoiceRecipientBindingProof proof,Guid requestId,CancellationToken ct);
    Task RevokeBindingAsync(int storeId,Guid bindingId,int version,int actorUserId,string reason,CancellationToken ct);
    Task<IReadOnlyList<InvoiceRecipientBindingSnapshot>> BindingsAsync(int storeId,int? customerId,CancellationToken ct);
    Task<IReadOnlyList<InvoiceNotificationListItem>> ListAsync(int storeId,string? state,int page,CancellationToken ct);
    Task<InvoiceNotificationDetail?> FindAsync(int storeId,Guid id,CancellationToken ct);
    Task<InvoiceNotificationSourcePreview> PreviewSourceAsync(int storeId,int customerId,string sourceType,int sourceId,string kind,CancellationToken ct);
    Task<Guid> EnqueueAsync(InvoiceNotificationEnqueueCommand command,CancellationToken ct);
}
public interface IInvoiceNotificationOutbox
{
    Task<InvoiceNotificationWorkItem?> ClaimAsync(CancellationToken ct);
    Task<InvoiceNotificationWorkItem> SavePreparedAsync(InvoiceNotificationWorkItem item,InvoicePreparedBundle bundle,CancellationToken ct);
    Task<bool> IsAuthorizedAsync(InvoiceNotificationWorkItem item,string currentConnectionBinding,CancellationToken ct);
    Task<bool> RevalidateAsync(InvoiceNotificationWorkItem item,string currentConnectionBinding,CancellationToken ct);
    Task<InvoiceNotificationWorkItem> AttachDiscoveredAcceptedAsync(InvoiceNotificationWorkItem item,Guid jobId,CancellationToken ct);
    Task RecordAcceptedAsync(InvoiceNotificationWorkItem item,Guid zaloJobId,CancellationToken ct);
    Task RecordRetryAsync(InvoiceNotificationWorkItem item,string detail,TimeSpan delay,CancellationToken ct);
    Task RecordReviewAsync(InvoiceNotificationWorkItem item,string detail,CancellationToken ct);
    Task RecordPartsOutcomeAsync(InvoiceNotificationWorkItem item,string state,string partsOutcomeJson,CancellationToken ct);
}
