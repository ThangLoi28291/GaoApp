using System.Text.Json.Serialization;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerInvoiceAdminCustomer(int Id,string Name,string? Code,string? PhoneHint);
public sealed record CustomerInvoiceAdminConnection(string Id,string Label,string AccountHint,bool Enabled);
public sealed record CustomerInvoiceAdminThread(Guid ConversationId,string DisplayName,long IncomingSequence,DateTimeOffset IncomingAt);
public sealed record CustomerInvoiceAdminEvidence(Guid ConversationId,long IncomingSequence,DateTimeOffset ObservedAtUtc,string DisplayName,bool Verified,string? BlockedReason);
public sealed record CustomerInvoiceAdminBinding(Guid Id,int CustomerId,string ConnectionId,Guid ConversationId,long IncomingSequence,int Version,DateTime VerifiedAtUtc,DateTime? RevokedAtUtc,bool Current,string? BlockedReason);
public sealed record CustomerInvoiceAdminSource(string SourceType,int SourceId,string Kind,string Code,string Label,DateTime At,decimal Total,string LegalOwner,string Status,bool CanQueue,string? BlockedReason);
public sealed record CustomerInvoiceAdminPreview(string SourceType,int SourceId,string Kind,string SourceVersion,string Code,string Label,string Status,decimal Total,string LegalOwner,bool CanQueue,string? BlockedReason);
public sealed record CustomerInvoiceAdminOutbox(Guid Id,int? CustomerId,string SourceType,int SourceId,string Kind,string Code,string State,string? Reason,DateTime CreatedAtUtc,DateTime? UpdatedAtUtc,Guid? ZaloJobId,int Attempts);
public sealed record CustomerInvoiceAdminPart(int Index,string Kind,string State,string? Text,string? FileName,long? ByteLength,string? ReceiptId,string? Reason);
public sealed record CustomerInvoiceAdminDetail(CustomerInvoiceAdminOutbox Item,CustomerInvoiceAdminBinding? Binding,
    string Status,string PaymentStatus,decimal Total,decimal PaidTotal,decimal ChangeDue,decimal BalanceDue,decimal? CurrentPoints,IReadOnlyList<CustomerInvoiceAdminPart> Parts);
public sealed record CustomerInvoiceNotificationAdminPage(string StoreName,CustomerStaff Staff,bool Enabled);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerInvoiceBindingCommand(Guid RequestId,int CustomerId,string ConnectionId,Guid ConversationId,long IncomingSequence,string Method,string EvidenceReference,bool Confirmed);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerInvoiceRevokeCommand(int Version,string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerInvoiceSendCommand(Guid RequestId,int CustomerId,string SourceType,int SourceId,string Kind,Guid BindingId,int BindingVersion,[property:JsonRequired]string ExpectedSourceVersion,bool Confirmed);

public interface ICustomerInvoiceNotificationAdmin
{
    Task<IReadOnlyList<CustomerInvoiceAdminCustomer>> CustomersAsync(CustomerStaff who,string query,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminConnection>> ConnectionsAsync(CustomerStaff who,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminThread>> ConversationsAsync(CustomerStaff who,string connection,string query,CancellationToken ct);
    Task<CustomerInvoiceAdminEvidence> InspectAsync(CustomerStaff who,string connection,Guid conversation,long sequence,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminBinding>> BindingsAsync(CustomerStaff who,int customer,CancellationToken ct);
    Task<CustomerInvoiceAdminBinding> VerifyAsync(CustomerStaff who,CustomerInvoiceBindingCommand command,CancellationToken ct);
    Task RevokeAsync(CustomerStaff who,Guid binding,CustomerInvoiceRevokeCommand command,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminSource>> SourcesAsync(CustomerStaff who,int customer,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminSource>> LookupSourcesAsync(CustomerStaff who,int customer,string query,CancellationToken ct);
    Task<CustomerInvoiceAdminPreview> PreviewAsync(CustomerStaff who,int customer,string sourceType,int sourceId,string kind,CancellationToken ct);
    Task<IReadOnlyList<CustomerInvoiceAdminOutbox>> OutboxAsync(CustomerStaff who,string? state,int page,CancellationToken ct);
    Task<CustomerInvoiceAdminDetail?> DetailAsync(CustomerStaff who,Guid id,CancellationToken ct);
    Task<Guid> EnqueueAsync(CustomerStaff who,CustomerInvoiceSendCommand command,CancellationToken ct);
}

