using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptPricingRuleConfiguration : IEntityTypeConfiguration<PurchaseReceiptPricingRule>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptPricingRule> b)
    {
        PricingPlanMapping.Common(b);
        b.HasAlternateKey(x => new { x.StoreId, x.PricingPlanId, x.Id });
        b.Property(x => x.RuleKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ProgramKey).HasMaxLength(64).IsRequired();
        b.HasOne(x => x.GiftBillLine).WithMany().HasForeignKey(x => new { x.StoreId, x.PricingPlanId, x.GiftBillLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.PricingPlanId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
        b.Property(x => x.GiftFactor).HasPrecision(18, 4);
        b.Property(x => x.GiftQuantity).HasPrecision(28, 9);
        b.HasOne(x => x.PricingPlan).WithMany(x => x.Rules).HasForeignKey(x => new { x.StoreId, x.PricingPlanId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.GiftPlanLine).WithMany().HasForeignKey(x => new { x.StoreId, x.PricingPlanId, x.GiftPlanLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.PricingPlanId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingPlanId, x.RuleKey }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.ToTable("PurchaseReceiptPricingRule", t => t.HasCheckConstraint("CK_PurchaseReceiptPricingRule_Type",
            "[Type] = 1 AND ([GiftMode] = 1 OR [GiftMode] = 2) AND [GiftPlanLineId] IS NOT NULL AND [GiftUnitId] IS NOT NULL AND [GiftFactor] > 0 AND [GiftQuantity] > 0 AND [DiscountPercent] = 0 OR [Type] = 2 AND [GiftPlanLineId] IS NULL AND [GiftMode] IS NULL AND [DiscountPercent] > 0 AND [DiscountPercent] < 100 OR [Type] = 3 AND [GiftPlanLineId] IS NULL AND [GiftMode] IS NULL AND [GiftBillLineId] IS NULL AND [GiftUnitId] IS NULL AND [GiftFactor] = 0 AND [GiftQuantity] = 0 AND [DiscountPercent] = 0 AND [Amount] > 0"));
    }
}
