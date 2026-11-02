using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerMarketingConsentConfiguration : IEntityTypeConfiguration<CustomerMarketingConsent>
{
    public void Configure(EntityTypeBuilder<CustomerMarketingConsent> b)
    {
        b.ToTable("CustomerMarketingConsents", t =>
        {
            t.HasCheckConstraint("CK_CustomerMarketingConsents_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [Channel] = N'Zalo' AND ([ChangedByUserId] IS NULL OR [ChangedByUserId] > 0) AND LEN([Source]) > 0");
        });
        b.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.EvidenceReference).HasMaxLength(250);
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.Channel }).IsUnique();
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

public sealed class CustomerMarketingConsentEventConfiguration : IEntityTypeConfiguration<CustomerMarketingConsentEvent>
{
    public void Configure(EntityTypeBuilder<CustomerMarketingConsentEvent> b)
    {
        b.ToTable("CustomerMarketingConsentEvents", t =>
        {
            t.HasCheckConstraint("CK_CustomerMarketingConsentEvents_Values", "[StoreId] > 0 AND [CustomerId] > 0 AND [Channel] = N'Zalo' AND ([RecordedByUserId] IS NULL OR [RecordedByUserId] > 0) AND LEN([Source]) > 0");
        });
        b.Property(x => x.Channel).HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.EvidenceReference).HasMaxLength(250);
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.Channel, x.RecordedAtUtc });
        b.HasIndex(x => x.CustomerId);
        b.HasIndex(x => new { x.StoreId, x.Channel, x.EvidenceReference }).IsUnique()
            .HasFilter("[Source] = N'ZaloKeyword' AND [EvidenceReference] IS NOT NULL");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
