using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class OrderInventoryIssueLineAllocationConfiguration : IEntityTypeConfiguration<OrderInventoryIssueLineAllocation>
{
    public void Configure(EntityTypeBuilder<OrderInventoryIssueLineAllocation> builder)
    {
        builder.ToTable("OrderInventoryIssueLineAllocations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllocatedQuantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.HasIndex(x => new
        {
            x.OrderInventoryIssueId,
            x.OrderInventoryIssueLineId,
            x.IsDeleted
        });

        builder.HasIndex(x => x.InventoryTransactionId);

        builder.HasIndex(x => x.InventoryCostLayerId);

        builder.HasIndex(x => x.InventoryCostLayerAllocationId);

        builder.HasOne(x => x.OrderInventoryIssue)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.OrderInventoryIssueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OrderInventoryIssueLine)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.OrderInventoryIssueLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryTransaction)
            .WithMany()
            .HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryCostLayer)
            .WithMany()
            .HasForeignKey(x => x.InventoryCostLayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryCostLayerAllocation)
            .WithMany()
            .HasForeignKey(x => x.InventoryCostLayerAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}