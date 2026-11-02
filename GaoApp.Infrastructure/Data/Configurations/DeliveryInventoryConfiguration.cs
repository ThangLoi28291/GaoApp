using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class DeliveryInventoryConfiguration : IEntityTypeConfiguration<DeliveryReturnedCostFragment>, IEntityTypeConfiguration<InventoryCostLayerAllocation>,
    IEntityTypeConfiguration<InventoryTransaction>, IEntityTypeConfiguration<InventoryValuationEntry>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> b)
        => b.ToTable(t => t.HasTrigger("TR_InventoryTransactions_DeliveryProvisional"));
    public void Configure(EntityTypeBuilder<InventoryValuationEntry> b)
        => b.ToTable(t => t.HasTrigger("TR_InventoryValuationEntries_DeliveryProvisional"));
    public void Configure(EntityTypeBuilder<InventoryCostLayerAllocation> b)
    {
        // This alias deliberately excludes nullable LayerId so historic provisional rows retain null.
        b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.InventoryValuationEntryId });
        b.ToTable(t => t.HasTrigger("TR_InventoryCostLayerAllocations_BudgetSource"));
        b.ToTable(t => t.HasTrigger("TR_InventoryCostLayerAllocations_DeliveryProvisional"));
    }
    public void Configure(EntityTypeBuilder<DeliveryReturnedCostFragment> b)
    {
        b.ToTable("DeliveryReturnedCostFragments", t => {
            t.HasTrigger("TR_DeliveryReturnedCostFragments_Immutable"); t.HasTrigger("TR_DeliveryReturnedCostFragments_StockSource");
            t.HasCheckConstraint("CK_DeliveryReturnedCostFragments_Interval", "[IsDeleted]=0 AND [StartQuantity]>=0 AND [EndQuantity]>[StartQuantity] AND [BaseQuantity]=[EndQuantity]-[StartQuantity] AND [UnitCost]>=0 AND [CostAmount]>=0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        foreach (var name in new[] { "StartQuantity", "EndQuantity", "BaseQuantity", "CostAmount" }) b.Property<decimal>(name).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 6);
        b.Property(x => x.IsProvisional).HasDefaultValue(false);
        b.HasIndex(x => new { x.StoreId, x.DispatchCostFragmentId, x.StartQuantity }).IsUnique();
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryDispatchCostFragment>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.DispatchCostFragmentId, x.SourceWarehouseId, x.SourceLegalEntityId, x.VariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId, x.VariantId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryJournalEntry>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryJournalEntryId, x.SourceWarehouseId, x.SourceLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryValuationEntry>().WithMany().HasForeignKey(x => new { x.StoreId, x.InventoryValuationEntryId, x.InventoryTransactionId, x.SourceWarehouseId, x.VariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.InventoryTransactionId, x.WarehouseId, x.ProductVariantId }).OnDelete(DeleteBehavior.Restrict);
    }
}
