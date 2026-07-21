using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// EF configuration cho allocation giữa valuation entry và FIFO cost layer.
/// </summary>
public class InventoryCostLayerAllocationConfiguration : IEntityTypeConfiguration<InventoryCostLayerAllocation>
{
    public void Configure(EntityTypeBuilder<InventoryCostLayerAllocation> builder)
    {
        builder.ToTable("InventoryCostLayerAllocations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.UnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 4);

        builder.Property(x => x.ResolvedQuantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.ResolvedAmount)
            .HasPrecision(18, 4);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.InventoryValuationEntryId)
            .HasDatabaseName("IX_InventoryCostLayerAllocations_ValuationEntryId");

        builder.HasIndex(x => x.InventoryCostLayerId)
            .HasDatabaseName("IX_InventoryCostLayerAllocations_CostLayerId");

        builder.HasIndex(x => x.ReverseOfAllocationId)
            .HasDatabaseName("IX_InventoryCostLayerAllocations_ReverseOfAllocationId");

        builder.HasIndex(x => x.ResolvedByInventoryCostLayerId)
            .HasDatabaseName("IX_InventoryCostLayerAllocations_ResolvedByLayerId");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.IsProvisional,
            x.IsResolved,
            x.Id
        })
        .HasDatabaseName("IX_InventoryCostLayerAllocations_OpenProvisional");

        builder.HasOne(x => x.InventoryValuationEntry)
            .WithMany(x => x.CostLayerAllocations)
            .HasForeignKey(x => x.InventoryValuationEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryCostLayer)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.InventoryCostLayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReverseOfAllocation)
            .WithMany(x => x.ReverseChildren)
            .HasForeignKey(x => x.ReverseOfAllocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ResolvedByInventoryCostLayer)
            .WithMany()
            .HasForeignKey(x => x.ResolvedByInventoryCostLayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}