using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InventoryValuationEntryConfiguration : IEntityTypeConfiguration<InventoryValuationEntry>
{
    public void Configure(EntityTypeBuilder<InventoryValuationEntry> builder)
    {
        builder.ToTable("InventoryValuationEntries");

        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.StoreId, x.EntryType, x.OccurredAtUtc });

        builder.Property(x => x.ReferenceId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ReferenceSubKey)
            .HasMaxLength(100);

        builder.Property(x => x.SourceReferenceSubKey)
            .HasMaxLength(100);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 4);

        builder.Property(x => x.UnitCost)
            .HasPrecision(18, 6)
            .HasDefaultValue(0m);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.RunningQtyAfter)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.RunningValueAfter)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.RunningAverageUnitCostAfter)
            .HasPrecision(18, 6)
            .HasDefaultValue(0m);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.IsProvisional)
            .HasDefaultValue(false);

        // =========================
        // Index phục vụ đọc ledger theo thời gian
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.OccurredAtUtc,
            x.Id
        });

        // =========================
        // Index truy từ InventoryTransaction -> valuation entries
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.InventoryTransactionId
        });

        // =========================
        // Index phục vụ revaluation lookup
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.RevaluationOfEntryId
        });

        // =========================
        // Index phục vụ reverse/mirror lookup
        // return / void / transfer in mức 2
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.SourceValuationEntryId
        });

        // =========================
        // Index reference chuẩn mức 2
        // có thêm ReferenceSubKey để tách fragment
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ReferenceType,
            x.ReferenceId,
            x.ReferenceLineId,
            x.ReferenceSubKey,
            x.EntryType
        });

        // =========================
        // Index phục vụ layer-based FIFO
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.InventoryCostLayerId
        });

        // =========================
        // Index phục vụ lookup provisional outbound cần finalize
        // =========================
        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.EntryType,
            x.IsProvisional,
            x.CostFinalizedAtUtc,
            x.OccurredAtUtc,
            x.Id
        });

        builder.HasOne(x => x.InventoryTransaction)
            .WithMany(x => x.ValuationEntries)
            .HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InventoryCostLayer)
            .WithMany()
            .HasForeignKey(x => x.InventoryCostLayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RevaluationOfEntry)
            .WithMany(x => x.RevaluationChildren)
            .HasForeignKey(x => x.RevaluationOfEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.SourceValuationEntry)
            .WithMany(x => x.ReverseChildren)
            .HasForeignKey(x => x.SourceValuationEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
