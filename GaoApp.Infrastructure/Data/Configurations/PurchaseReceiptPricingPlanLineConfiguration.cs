using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptPricingPlanLineConfiguration : IEntityTypeConfiguration<PurchaseReceiptPricingPlanLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptPricingPlanLine> b)
    {
        PricingPlanMapping.Common(b);
        b.HasAlternateKey(x => new { x.StoreId, x.PricingPlanId, x.Id });
        b.Property(x => x.PhysicalFactorSnapshot).HasPrecision(18, 4);
        b.Property(x => x.BillFactor).HasPrecision(18, 4);
        b.Property(x => x.PhysicalQuantitySnapshot).HasPrecision(18, 3);
        b.Property(x => x.PhysicalBaseQuantitySnapshot).HasPrecision(18, 3);
        b.Property(x => x.BillQuantity).HasPrecision(28, 9);
        b.Property(x => x.PurchasedBaseQuantity).HasPrecision(18, 3);
        b.Property(x => x.GiftBaseQuantity).HasPrecision(18, 3);
        b.Property(x => x.BillUnitPriceBeforeVat).HasPrecision(28, 12);
        b.Property(x => x.ReceiptLineRowVersionSnapshot).HasMaxLength(8).IsRequired();
        b.HasOne(x => x.PricingPlan).WithMany(x => x.Lines).HasForeignKey(x => new { x.StoreId, x.PricingPlanId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.StockDocumentLine).WithMany().HasForeignKey(x => x.StockDocumentLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingPlanId, x.StockDocumentLineId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.ToTable("PurchaseReceiptPricingPlanLine", t =>
        {
            t.HasCheckConstraint("CK_PurchaseReceiptPricingPlanLine_Quantity", "[PhysicalQuantitySnapshot] > 0 AND [PhysicalBaseQuantitySnapshot] > 0 AND [PhysicalFactorSnapshot] > 0 AND [BillFactor] > 0 AND [BillQuantity] >= 0 AND [PurchasedBaseQuantity] >= 0 AND [GiftBaseQuantity] >= 0");
            t.HasCheckConstraint("CK_PurchaseReceiptPricingPlanLine_Money", "[BillUnitPriceBeforeVat] >= 0 AND [BaselineAmount] >= 0 AND [GiftBurden] >= 0 AND [PurchasedAmount] >= 0 AND [GiftAmount] >= 0 AND [FinalAmountBeforeVat] > 0");
        });
    }
}
