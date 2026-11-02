using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;
public sealed class InvoiceRecipientBindingConfiguration:IEntityTypeConfiguration<InvoiceRecipientBinding>
{
    public void Configure(EntityTypeBuilder<InvoiceRecipientBinding>b)
    {
        b.ToTable("InvoiceRecipientBindings",t=>t.HasCheckConstraint("CK_InvoiceRecipientBindings_Proof","[StoreId]>0 AND [CustomerId]>0 AND [Version]>0 AND [IncomingSequence]>0 AND [VerifiedByUserId]>0"));
        b.HasKey(x=>x.Id);b.Property(x=>x.RowVersion).IsRowVersion();
        b.Property(x=>x.ConnectionId).HasMaxLength(48);b.Property(x=>x.ConnectionBinding).HasMaxLength(64);
        b.Property(x=>x.NativePeer).HasMaxLength(128);b.Property(x=>x.Phone).HasMaxLength(20);
        b.Property(x=>x.VerificationMethod).HasMaxLength(40);b.Property(x=>x.EvidenceReference).HasMaxLength(250);
        b.Property(x=>x.RevocationReason).HasMaxLength(250);
        b.HasIndex(x=>new{x.StoreId,x.CustomerId}).IsUnique().HasFilter("[RevokedAtUtc] IS NULL");
        b.HasIndex(x=>new{x.StoreId,x.VerificationRequestId}).IsUnique();
        b.HasIndex(x=>new{x.StoreId,x.ConnectionId,x.ConversationId}).IsUnique().HasFilter("[RevokedAtUtc] IS NULL");
        b.HasOne<Customer>().WithMany().HasForeignKey(x=>x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(x=>x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class InvoiceNotificationOutboxConfiguration:IEntityTypeConfiguration<InvoiceNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<InvoiceNotificationOutbox>b)
    {
        b.ToTable("InvoiceNotificationOutbox",t=>{
            t.HasCheckConstraint("CK_InvoiceNotificationOutbox_Source","[StoreId]>0 AND [SourceId]>0 AND [SourceType] IN ('Order','Invoice') AND [Kind] IN ('Receipt','OfficialPdf','OfficialZipXml','OfficialXml','OfficialLink','CorrectionPdf','CorrectionZipXml','CorrectionXml','CorrectionLink','DraftPreview')");
            t.HasCheckConstraint("CK_InvoiceNotificationOutbox_State","[State] IN ('Pending','Dispatching','Accepted','Sent','PartiallySent','NeedsReview','Failed','Blocked','Cancelled')");
            t.HasCheckConstraint("CK_InvoiceNotificationOutbox_Json","ISJSON([SnapshotJson])=1 AND ([BindingSnapshotJson] IS NULL OR ISJSON([BindingSnapshotJson])=1) AND ([PreparedJson] IS NULL OR ISJSON([PreparedJson])=1)");
        });
        b.HasKey(x=>x.Id);b.Property(x=>x.RowVersion).IsRowVersion();
        b.Property(x=>x.SourceType).HasMaxLength(16);b.Property(x=>x.SourceVersion).HasMaxLength(64);b.Property(x=>x.Kind).HasMaxLength(32);
        b.Property(x=>x.Code).HasMaxLength(100);b.Property(x=>x.SnapshotSha256).HasMaxLength(64);b.Property(x=>x.IdempotencyKey).HasMaxLength(100);
        b.Property(x=>x.State).HasMaxLength(24);b.Property(x=>x.Detail).HasMaxLength(250);
        b.HasIndex(x=>new{x.StoreId,x.SourceType,x.SourceId,x.SourceVersion,x.Kind}).IsUnique();
        b.HasIndex(x=>new{x.StoreId,x.RequestId}).IsUnique();b.HasIndex(x=>new{x.State,x.NextAttemptAtUtc});
        b.HasOne<Store>().WithMany().HasForeignKey(x=>x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InvoiceRecipientBinding>().WithMany().HasForeignKey(x=>x.BindingId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class InvoiceNotificationAuditConfiguration:IEntityTypeConfiguration<InvoiceNotificationAudit>
{
    public void Configure(EntityTypeBuilder<InvoiceNotificationAudit>b)
    {
        b.ToTable("InvoiceNotificationAudit");b.HasKey(x=>x.Id);b.Property(x=>x.Action).HasMaxLength(50);b.Property(x=>x.Detail).HasMaxLength(250);
        b.HasIndex(x=>new{x.StoreId,x.CreatedAtUtc});
    }
}

public sealed class InvoiceNotificationArtifactProofConfiguration:IEntityTypeConfiguration<InvoiceHead>
{
    public void Configure(EntityTypeBuilder<InvoiceHead>b)
    {
        b.Property(x=>x.OfficialPdfSha256).HasMaxLength(64);
        b.Property(x=>x.OfficialZipXmlSha256).HasMaxLength(64);
        b.ToTable("InvoiceHeads",t=>t.HasCheckConstraint("CK_InvoiceHeads_NotificationArtifactProof",
            "([OfficialPdfSha256] IS NULL AND [OfficialPdfByteLength] IS NULL OR LEN([OfficialPdfSha256])=64 AND [OfficialPdfByteLength]>0) AND ([OfficialZipXmlSha256] IS NULL AND [OfficialZipXmlByteLength] IS NULL OR LEN([OfficialZipXmlSha256])=64 AND [OfficialZipXmlByteLength]>0)"));
    }
}
