using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.ToTable("InventoryBalances");

        builder.HasKey(x => x.Id);

        // =========================================================
        // Quantity fields
        // =========================================================
        builder.Property(x => x.OnHandQty)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.ReservedQty)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        // =========================================================
        // Cost / valuation foundation
        // =========================================================
        builder.Property(x => x.InventoryValue)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.AverageUnitCost)
            .HasPrecision(18, 6)
            .HasDefaultValue(0m);

        builder.Property(x => x.LastInboundUnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.LastInboundAtUtc);

        builder.Property(x => x.LastValuationAtUtc);

        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.ProductVariantId })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId });
        builder.HasIndex(x => new { x.StoreId, x.WarehouseId });

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.InventoryBalances)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany(x => x.InventoryBalances)
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}