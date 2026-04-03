using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration cho FIFO cost layer.
/// </summary>
public class InventoryCostLayerConfiguration : IEntityTypeConfiguration<InventoryCostLayer>
{
    public void Configure(EntityTypeBuilder<InventoryCostLayer> builder)
    {
        builder.ToTable("InventoryCostLayers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ReferenceSubKey)
            .HasMaxLength(100);

        builder.Property(x => x.OriginalQuantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.RemainingQuantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.ResolvedProvisionalQty)
            .HasPrecision(18, 4);

        builder.Property(x => x.RemainingOpenProvisionalQty)
            .HasPrecision(18, 4);

        builder.Property(x => x.UnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.OccurredAtUtc,
            x.Id
        })
        .HasDatabaseName("IX_InventoryCostLayers_FIFO");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.RemainingQuantity
        })
        .HasDatabaseName("IX_InventoryCostLayers_Open");

        builder.HasIndex(x => x.InventoryTransactionId)
            .HasDatabaseName("IX_InventoryCostLayers_InventoryTransactionId");

        builder.HasIndex(x => x.InventoryValuationEntryId)
            .HasDatabaseName("IX_InventoryCostLayers_InventoryValuationEntryId");

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryTransaction)
            .WithMany()
            .HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryValuationEntry)
            .WithMany()
            .HasForeignKey(x => x.InventoryValuationEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}