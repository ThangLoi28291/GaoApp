using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptBillLineConfiguration : IEntityTypeConfiguration<PurchaseReceiptBillLine>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptBillLine> b)
    {
        PricingPlanMapping.Common(b);
        b.HasAlternateKey(x => new { x.StoreId, x.PricingPlanId, x.Id });
        b.Property(x => x.BillLineKey).HasMaxLength(64).IsRequired();
        b.Property(x => x.BillUnitName).HasMaxLength(200).IsRequired();
        b.Property(x => x.BillFactor).HasPrecision(18, 4);
        b.Property(x => x.BillQuantity).HasPrecision(28, 9);
        b.Property(x => x.BillUnitPriceBeforeVat).HasPrecision(28, 12);
        b.HasOne(x => x.PricingPlan).WithMany(x => x.BillLines).HasForeignKey(x => new { x.StoreId, x.PricingPlanId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.PricingPlanId, x.BillLineKey }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new { x.StoreId, x.PricingPlanId, x.LineNo }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.ToTable("PurchaseReceiptBillLine", t => t.HasCheckConstraint("CK_PurchaseReceiptBillLine_Input",
            "[LineNo] > 0 AND [BillFactor] > 0 AND [BillQuantity] > 0 AND ([IsGift] = 1 AND [BillUnitPriceBeforeVat] = 0 OR [IsGift] = 0 AND [BillUnitPriceBeforeVat] > 0)"));
    }
}
