using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InventoryCostBudgetDebitConfiguration : IEntityTypeConfiguration<InventoryCostBudgetDebit>
{
    public void Configure(EntityTypeBuilder<InventoryCostBudgetDebit> b)
    {
        b.ToTable("InventoryCostBudgetDebits", t => {
            t.HasTrigger("TR_InventoryCostBudgetDebits_Immutable"); t.HasTrigger("TR_InventoryCostBudgetDebits_Source");
            t.HasCheckConstraint("CK_InventoryCostBudgetDebits_Interval", "[IsDeleted]=0 AND [AnchorQuantity]>0 AND [AnchorAmount]>=0 AND [StartQuantity]>=0 AND [EndQuantity]>[StartQuantity] AND [EndQuantity]<=[AnchorQuantity] AND [UnitCost]>=0 AND [Amount]>=0 AND (([IsProvisionalResolution]=0 AND [ResolutionStartQuantity] IS NULL AND [ResolutionEndQuantity] IS NULL AND [RevaluationEntryId] IS NULL) OR ([IsProvisionalResolution]=1 AND [ResolutionStartQuantity] IS NOT NULL AND [ResolutionEndQuantity] IS NOT NULL AND [ResolutionStartQuantity]>=0 AND [ResolutionEndQuantity]>[ResolutionStartQuantity] AND [ResolutionEndQuantity]-[ResolutionStartQuantity]=[EndQuantity]-[StartQuantity]))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        foreach (var name in new[] { "AnchorQuantity", "AnchorAmount", "StartQuantity", "EndQuantity", "Amount" }) b.Property<decimal>(name).HasPrecision(18, 4);
        b.Property(x => x.ResolutionStartQuantity).HasPrecision(18, 4); b.Property(x => x.ResolutionEndQuantity).HasPrecision(18, 4); b.Property(x => x.UnitCost).HasPrecision(18, 6);
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId, x.SourceLegalEntityId, x.InventoryCostLayerId, x.InventoryTransactionId, x.InventoryValuationEntryId, x.InventoryCostLayerAllocationId });
        b.HasIndex(x => new { x.StoreId, x.InventoryCostLayerId, x.StartQuantity }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.InventoryCostLayerAllocationId, x.ResolutionStartQuantity }).IsUnique().HasFilter("[IsProvisionalResolution]=1");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Layer).WithMany().HasForeignKey(x => new { x.StoreId, x.InventoryCostLayerId, x.WarehouseId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ValuationEntry).WithMany(x => x.CostBudgetDebits).HasForeignKey(x => new { x.StoreId, x.InventoryValuationEntryId, x.WarehouseId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Allocation).WithMany().HasForeignKey(x => new { x.StoreId, x.InventoryCostLayerAllocationId, x.InventoryValuationEntryId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.InventoryValuationEntryId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryTransaction>().WithMany().HasForeignKey(x => new { x.StoreId, x.InventoryTransactionId, x.WarehouseId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.WarehouseId, x.ProductVariantId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.StoreId, x.WarehouseId, x.SourceLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.LegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryValuationEntry>().WithMany().HasForeignKey(x => new { x.StoreId, x.RevaluationEntryId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
