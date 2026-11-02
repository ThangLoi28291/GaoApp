namespace GaoApp.Application.InvoiceNotifications;

public sealed class InvoiceOfficialDocumentLinkOptions
{
    public const string SectionName = "InvoiceNotifications:OfficialLinks";
    public string PublicBaseUrl { get; set; } = "";
}
public interface IInvoiceWorkItemBundleBuilder
{
    Task<InvoicePreparedBundle> BuildAsync(InvoiceNotificationWorkItem item, CancellationToken ct);
}
public sealed record InvoiceOfficialDocumentLinkMetadata(string FileName, string ContentType, long ByteLength, string Sha256);
public interface IInvoiceOfficialDocumentLinkFactory
{
    Task<string> CreateAsync(InvoiceNotificationWorkItem item, InvoiceOfficialDocumentLinkMetadata pdf, CancellationToken ct);
}
public sealed class InvoiceOfficialDocumentLinkNotConfiguredException : InvalidOperationException
{
    public InvoiceOfficialDocumentLinkNotConfiguredException() : base("OfficialLinkNotConfigured") { }
}
/// <summary>Protected capability payload. No provider reservation code, business lines or storage paths are included.</summary>
public sealed record InvoiceOfficialDocumentLinkGrant(int SchemaVersion, Guid NotificationId, int StoreId, int CustomerId,
    string SourceType, int SourceId, string SourceVersion, string Kind, Guid BindingId, int BindingVersion,
    string Audience, DateTimeOffset ExpiresAtUtc, InvoiceOfficialDocumentLinkMetadata Pdf);
public sealed record InvoiceOfficialDocumentLinkAccess(InvoiceDocumentSnapshot Snapshot, InvoiceRecipientBindingSnapshot Binding);
public interface IInvoiceOfficialDocumentLinkAuthorizer
{
    Task<InvoiceOfficialDocumentLinkAccess?> AuthorizeAsync(InvoiceOfficialDocumentLinkGrant grant, CancellationToken ct);
}
