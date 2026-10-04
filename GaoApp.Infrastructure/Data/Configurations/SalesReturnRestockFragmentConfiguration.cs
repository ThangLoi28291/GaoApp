using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class SalesReturnRestockFragmentConfiguration : IEntityTypeConfiguration<SalesReturnRestockFragment>
{
    public void Configure(EntityTypeBuilder<SalesReturnRestockFragment> b)
    {
        b.ToTable("SalesReturnRestockFragments", t => {
            t.HasCheckConstraint("CK_SalesReturnRestockFragments_Quantity", "[BaseQuantity] > 0");
            t.HasCheckConstraint("CK_SalesReturnRestockFragments_Completion", "([InventoryTransactionId] IS NULL AND [CompletedAtUtc] IS NULL AND [CompletedByUserId] IS NULL) OR ([InventoryTransactionId] IS NOT NULL AND [CompletedAtUtc] IS NOT NULL AND [CompletedByUserId] IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        b.HasIndex(x => new {x.StoreId, x.SalesReturnLineId, x.SourceValuationEntryId}).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasIndex(x => new {x.StoreId, x.CompletedAtUtc, x.IsDeleted});
        b.HasIndex(x => new {x.StoreId, x.SourceValuationEntryId, x.IsDeleted});
        b.HasOne(x => x.SalesReturnLine).WithMany().HasForeignKey(x => x.SalesReturnLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceValuationEntry).WithMany().HasForeignKey(x => x.SourceValuationEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AllocationReversal).WithMany().HasForeignKey(x => x.AllocationReversalId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InventoryTransaction).WithMany().HasForeignKey(x => x.InventoryTransactionId).OnDelete(DeleteBehavior.Restrict);
    }
}
