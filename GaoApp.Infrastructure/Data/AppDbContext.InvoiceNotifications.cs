using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data;
public partial class AppDbContext
{
    public DbSet<InvoiceRecipientBinding> InvoiceRecipientBindings=>Set<InvoiceRecipientBinding>();
    public DbSet<InvoiceNotificationOutbox> InvoiceNotificationOutbox=>Set<InvoiceNotificationOutbox>();
    public DbSet<InvoiceNotificationAudit> InvoiceNotificationAudit=>Set<InvoiceNotificationAudit>();

    private void ProtectInvoiceNotificationEvidence()
    {
        foreach(var entry in ChangeTracker.Entries<InvoiceNotificationAudit>())
            if(entry.State is EntityState.Modified or EntityState.Deleted)throw new InvalidOperationException("Invoice notification audit is append-only.");
        foreach(var entry in ChangeTracker.Entries<InvoiceNotificationOutbox>())
        {
            if(entry.State==EntityState.Deleted)throw new InvalidOperationException("Invoice notification evidence cannot be deleted.");
            if(entry.State!=EntityState.Modified)continue;
            foreach(var name in new[]{"Id","StoreId","CustomerId","SourceType","SourceId","SourceVersion","Kind","Code","SnapshotJson","SnapshotSha256","RequestId","IdempotencyKey","CreatedAtUtc","CreatedByUserId"})
                if(entry.Property(name).IsModified)throw new InvalidOperationException("Invoice notification snapshots are immutable.");
            foreach(var name in new[]{"BindingId","BindingVersion","BindingSnapshotJson","PreparedJson"})
                if(entry.Property(name).IsModified&&entry.Property(name).OriginalValue is not null)throw new InvalidOperationException("Prepared notification and recipient evidence cannot be replaced.");
        }
        foreach(var entry in ChangeTracker.Entries<InvoiceRecipientBinding>())
        {
            if(entry.State==EntityState.Deleted)throw new InvalidOperationException("Recipient binding evidence cannot be deleted.");
            if(entry.State!=EntityState.Modified)continue;
            foreach(var name in new[]{"Id","StoreId","CustomerId","VerificationRequestId","ConnectionId","ConnectionBinding","SenderProfileId","ConversationId","IncomingSequence","NativePeer","Phone","VerifiedByUserId","VerifiedAtUtc","VerificationMethod","EvidenceReference"})
                if(entry.Property(name).IsModified)throw new InvalidOperationException("Verified recipient evidence is immutable; revoke and verify a new binding.");
        }
    }

    private async Task StageInvoiceNotificationsAsync(CancellationToken ct)
    {
        if(!_invoiceNotificationsCaptureEnabled)return;
        var changed=ChangeTracker.Entries<InvoiceHead>().Where(e=>e.State==EntityState.Modified&&
            new[]{"ProviderStatus","OfficialPdfStatus","OfficialPdfSha256","OfficialPdfByteLength","OfficialZipXmlStatus","OfficialZipXmlSha256","OfficialZipXmlByteLength"}.Any(n=>e.Property(n).IsModified)).Select(e=>e.Entity).ToArray();
        foreach(var invoice in changed)
        {
            if(invoice.StoreId<=0||invoice.Id<=0)continue;
            if(invoice.OfficialPdfStatus==InvoiceFileDownloadStatus.Downloaded&&invoice.OfficialPdfSha256 is not null)
                await EfInvoiceNotificationCapture.StageAsync(this,await InvoiceNotificationSnapshots.InvoiceAsync(this,invoice,invoice.CorrectionType is null?"OfficialPdf":"CorrectionPdf",ct),CurrentUserId,ct);
            if(invoice.OfficialZipXmlStatus==InvoiceFileDownloadStatus.Downloaded&&invoice.OfficialZipXmlSha256 is not null)
                await EfInvoiceNotificationCapture.StageAsync(this,await InvoiceNotificationSnapshots.InvoiceAsync(this,invoice,invoice.CorrectionType is null?"OfficialZipXml":"CorrectionZipXml",ct),CurrentUserId,ct);
        }
    }
}
