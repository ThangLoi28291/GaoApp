using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptPricingPlanConfiguration : IEntityTypeConfiguration<PurchaseReceiptPricingPlan>
{
    public void Configure(EntityTypeBuilder<PurchaseReceiptPricingPlan> b)
    {
        PricingPlanMapping.Common(b);
        b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.Property(x => x.PhysicalDependencyHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        b.Property(x => x.ReceiptRowVersionSnapshot).HasMaxLength(8).IsRequired();
        b.Property(x => x.GlobalDiscountPercent).HasPrecision(9, 4);
        b.HasOne(x => x.StockDocument).WithMany().HasForeignKey(x => x.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.StockDocumentId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.ToTable("PurchaseReceiptPricingPlan", t =>
        {
            t.HasCheckConstraint("CK_PurchaseReceiptPricingPlan_State", "[State] = 1 OR [State] = 2 OR [State] = 3");
            t.HasCheckConstraint("CK_PurchaseReceiptPricingPlan_Money", "[ActualBillTotal] > 0 AND [SystemTotal] > 0 AND [GlobalDiscountPercent] >= 0 AND [GlobalDiscountPercent] < 100");
        });
    }
}

internal static class PricingPlanMapping
{
    internal static void Common<T>(EntityTypeBuilder<T> b) where T : BaseStoreEntity
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        foreach (var property in typeof(T).GetProperties().Where(x => x.PropertyType == typeof(decimal)))
            b.Property<decimal>(property.Name).HasPrecision(18, 2);
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
}
