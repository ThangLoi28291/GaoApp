using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerMarketingCampaignConfiguration : IEntityTypeConfiguration<CustomerMarketingCampaign>
{
    public void Configure(EntityTypeBuilder<CustomerMarketingCampaign> b)
    {
        b.ToTable("CustomerMarketingCampaigns", t =>
        {
            t.HasCheckConstraint("CK_CustomerMarketingCampaigns_Values", "[StoreId] > 0 AND [RecipientCount] > 0 AND [CampaignKey] <> '00000000-0000-0000-0000-000000000000' AND [PreviewRequestId] <> '00000000-0000-0000-0000-000000000000' AND [SenderProfileId] <> '00000000-0000-0000-0000-000000000000' AND LEN([MessageText]) > 0 AND LEN([RequestFingerprint]) = 64 AND (([AssetBytes] IS NULL AND [AssetKind] IS NULL AND [AssetFileName] IS NULL AND [AssetContentType] IS NULL AND [AssetLength] IS NULL AND [AssetSha256] IS NULL) OR ([AssetBytes] IS NOT NULL AND [AssetKind] IN ('Image','File') AND [AssetFileName] IS NOT NULL AND [AssetContentType] IS NOT NULL AND [AssetLength] > 0 AND [AssetSha256] IS NOT NULL)) AND [Status] IN ('Draft','Scheduled','Dispatching','Completed','Cancelled','NeedsReview')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.MessageText).HasMaxLength(4000).IsRequired();
        b.Property(x => x.AssetKind).HasMaxLength(8);
        b.Property(x => x.AssetFileName).HasMaxLength(120);
        b.Property(x => x.AssetContentType).HasMaxLength(100);
        b.Property(x => x.AssetSha256).HasMaxLength(64);
        b.Property(x => x.CustomerGroup).HasMaxLength(30);
        b.Property(x => x.ConnectionId).HasMaxLength(48).IsRequired();
        b.Property(x => x.ConnectionBinding).HasMaxLength(64).IsRequired();
        b.Property(x => x.SnapshotSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasMaxLength(24).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(250);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.CampaignKey).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.PreviewRequestId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.ScheduledAtUtc });
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CustomerMarketingCampaignRecipientConfiguration : IEntityTypeConfiguration<CustomerMarketingCampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CustomerMarketingCampaignRecipient> b)
    {
        b.ToTable("CustomerMarketingCampaignRecipients", t =>
        {
            t.HasCheckConstraint("CK_CustomerMarketingCampaignRecipients_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [BindingVersion] > 0 AND [SenderProfileId] <> '00000000-0000-0000-0000-000000000000' AND [Status] IN ('Pending','Dispatching','Accepted','Sent','Skipped','Failed','NeedsReview')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ConnectionId).HasMaxLength(48).IsRequired();
        b.Property(x => x.CustomerNameSnapshot).HasMaxLength(200).IsRequired();
        b.Property(x => x.ConnectionBinding).HasMaxLength(64).IsRequired();
        b.Property(x => x.NativePeer).HasMaxLength(128).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasMaxLength(24).IsRequired();
        b.Property(x => x.Detail).HasMaxLength(250);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.CampaignId, x.CustomerId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.NextAttemptAtUtc });
        b.HasOne<CustomerMarketingCampaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InvoiceRecipientBinding>().WithMany().HasForeignKey(x => x.BindingId).OnDelete(DeleteBehavior.Restrict);
    }
}
