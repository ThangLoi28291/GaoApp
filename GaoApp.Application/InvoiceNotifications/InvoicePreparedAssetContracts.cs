namespace GaoApp.Application.InvoiceNotifications;

public interface IInvoicePreparedAssetReader
{
    Task<byte[]> ReadAsync(InvoiceNotificationWorkItem item, InvoicePreparedPart part, CancellationToken ct);
}

public sealed class InvoiceNotificationArtifactOptions
{
    public const string SectionName = "InvoiceNotifications:Artifacts";
    public string RootDirectory { get; set; } = "App_Data/InvoiceNotifications";
    public long MaximumStoredBytes { get; set; } = 1024L * 1024 * 1024;
    public bool IncludeReceiptImages { get; set; } = true;
}
