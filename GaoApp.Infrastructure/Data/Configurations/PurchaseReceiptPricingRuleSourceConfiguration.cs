using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptPricingRuleSourceConfiguration : IEntityTypeConfiguration<PurchaseReceiptPricingRuleSource>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptPricingRuleSource> b)
    {
        PricingPlanMapping.Common(b);
        b.HasOne(x => x.PricingRule).WithMany(x => x.Sources).HasForeignKey(x => new { x.StoreId, x.PricingPlanId, x.PricingRuleId })
            .HasPrincipalKey(x => new { x.StoreId, x.PricingPlanId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PricingPlanLine).WithMany().HasForeignKey(x => new { x.StoreId, x.PricingPlanId, x.PricingPlanLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.PricingPlanId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingRuleId, x.PricingPlanLineId }).IsUnique().HasFilter("[IsDeleted] = 0 AND [BillLineId] IS NULL");
        b.Property(x => x.ParticipatingQuantity).HasPrecision(28, 9);
        b.HasOne(x => x.BillLine).WithMany().HasForeignKey(x => new { x.StoreId, x.PricingPlanId, x.BillLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.PricingPlanId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingRuleId, x.BillLineId }).IsUnique().HasFilter("[IsDeleted] = 0 AND [BillLineId] IS NOT NULL");
        b.ToTable("PurchaseReceiptPricingRuleSource", t => t.HasCheckConstraint("CK_PurchaseReceiptPricingRuleSource_Quantity",
            "[BillLineId] IS NULL AND [ParticipatingQuantity] IS NULL OR [BillLineId] IS NOT NULL AND [ParticipatingQuantity] > 0"));
        b.ToTable("PurchaseReceiptPricingRuleSource", t => t.HasCheckConstraint("CK_PurchaseReceiptPricingRuleSource_Money",
            "[BaselineAmountSnapshot] > 0 AND [Amount] >= 0 AND [ResidualAmount] >= 0"));
    }
}
