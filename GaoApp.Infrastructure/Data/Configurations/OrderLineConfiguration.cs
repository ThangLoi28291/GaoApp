using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> b)
    {
        b.ToTable("OrderLines");

        b.HasKey(x => x.Id);

        // =========================================================
        // String fields
        // =========================================================
        b.Property(x => x.ItemName)
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.UnitName)
            .HasMaxLength(50);

        b.Property(x => x.Sku)
            .HasMaxLength(50);

        b.Property(x => x.Barcode)
            .HasMaxLength(50);

        b.Property(x => x.CostSnapshotNote)
            .HasMaxLength(1000);

        b.Property(x => x.SellingUnitName)
            .HasMaxLength(100);

        b.Property(x => x.BaseUnitName)
            .HasMaxLength(100);

        b.Property(x => x.ScannedBarcode)
            .HasMaxLength(100);

        // =========================================================
        // Quantity fields
        // =========================================================
        b.Property(x => x.Quantity)
            .HasPrecision(18, 4);

        b.Property(x => x.BaseQuantity)
            .HasPrecision(18, 4);

        // =========================================================
        // Conversion factor
        // =========================================================
        b.Property(x => x.Multiplier)
            .HasPrecision(18, 6)
            .HasDefaultValue(1m);

        // =========================================================
        // Selling price / discount / line amount
        // =========================================================
        b.Property(x => x.UnitPrice)
            .HasPrecision(18, 2);

        b.Property(x => x.LineDiscount)
            .HasPrecision(18, 2);

        b.Property(x => x.LineTotal)
            .HasPrecision(18, 2);

        // =========================================================
        // Cost snapshot fields
        // =========================================================
        b.Property(x => x.UnitCostSnapshot)
            .HasPrecision(18, 6);

        b.Property(x => x.ProvisionalUnitCost)
            .HasPrecision(18, 6);

        b.Property(x => x.LineCostTotal)
            .HasPrecision(18, 4);

        b.Property(x => x.GrossProfit)
            .HasPrecision(18, 4);

        b.Property(x => x.IsProvisionalCost)
            .HasDefaultValue(false);

        // =========================================================
        // Indexes
        // =========================================================
        b.HasIndex(x => new { x.StoreId, x.OrderId });

        b.HasIndex(x => new { x.StoreId, x.ProductId });

        b.HasIndex(x => new { x.StoreId, x.VariantId });

        // =========================================================
        // Query filter
        // =========================================================
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}