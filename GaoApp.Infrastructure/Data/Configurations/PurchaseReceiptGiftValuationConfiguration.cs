using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptGiftValuationConfiguration : IEntityTypeConfiguration<PurchaseReceiptGiftValuation>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptGiftValuation> b)
    {
        PricingPlanMapping.Common(b);
        b.Property(x => x.Factor).HasPrecision(18, 4);
        b.Property(x => x.UnitValueBeforeVat).HasPrecision(28, 12);
        b.HasOne(x => x.PricingPlan).WithMany(x => x.GiftValuations).HasForeignKey(x => new { x.StoreId, x.PricingPlanId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingPlanId, x.ProductVariantId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.ToTable("PurchaseReceiptGiftValuation", t => t.HasCheckConstraint("CK_PurchaseReceiptGiftValuation_Value",
            "[Factor] > 0 AND [UnitValueBeforeVat] > 0 AND ([Source] = 1 OR [Source] = 2 OR [Source] = 3)"));
    }
}
