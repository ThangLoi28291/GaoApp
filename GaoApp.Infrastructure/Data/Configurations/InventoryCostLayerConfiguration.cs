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
        builder.ToTable("InventoryCostLayers", t => {
            t.HasTrigger("TR_InventoryCostLayers_BudgetSource");
            t.HasCheckConstraint("CK_InventoryCostLayers_Budget", "([CostBudgetPolicy]=0 AND [BudgetAnchorQuantity] IS NULL AND [BudgetAnchorAmount] IS NULL AND [RemainingCostAmount] IS NULL) OR ([CostBudgetPolicy]=1 AND [BudgetAnchorQuantity] IS NOT NULL AND [BudgetAnchorQuantity]>0 AND [BudgetAnchorAmount] IS NOT NULL AND [BudgetAnchorAmount]>=0 AND [RemainingCostAmount] IS NOT NULL AND [RemainingCostAmount]>=0 AND [RemainingCostAmount]<=[BudgetAnchorAmount] AND [RemainingQuantity]>=0 AND [RemainingQuantity]<=[BudgetAnchorQuantity] AND [UnitCost]>=0)");
        });
        builder.Property(x => x.CostBudgetPolicy).HasDefaultValue((byte)0);
        builder.Property(x => x.BudgetAnchorQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BudgetAnchorAmount).HasPrecision(18, 4);
        builder.Property(x => x.RemainingCostAmount).HasPrecision(18, 4);
        builder.HasAlternateKey(x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId });

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
