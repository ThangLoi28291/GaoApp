using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReferenceId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasColumnType("varbinary(32)");

        builder.Property(x => x.QuantityChange)
            .HasPrecision(18, 4);

        builder.Property(x => x.BeforeQty)
            .HasPrecision(18, 4);

        builder.Property(x => x.AfterQty)
            .HasPrecision(18, 4);

        builder.Property(x => x.UnitCostSnapshot)
            .HasPrecision(18, 6)
            .HasDefaultValue(0m);

        builder.Property(x => x.TotalCost)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.BeforeInventoryValue)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.AfterInventoryValue)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        builder.Property(x => x.RunningAverageUnitCostAfter)
            .HasPrecision(18, 6)
            .HasDefaultValue(0m);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.IsProvisionalCost)
            .HasDefaultValue(false);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.OccurredAtUtc,
            x.Id
        });

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ReferenceType,
            x.ReferenceId,
            x.ReferenceLineId,
            x.TransactionType
        });

        builder.HasIndex(x => new
            {
                x.StoreId,
                x.IdempotencyKey
            })
            .HasDatabaseName(
                "UX_InventoryTransactions_StoreId_IdempotencyKey_Active")
            .IsUnique()
            .HasFilter(
                "[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");

        // =========================================================
        // Chốt rõ quan hệ Warehouse <-> InventoryTransactions
        // để EF không tự sinh shadow FK WarehouseId1
        // =========================================================
        builder.HasOne(x => x.Warehouse)
            .WithMany(w => w.InventoryTransactions)
            .HasForeignKey(x => x.WarehouseId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany(v => v.InventoryTransactions)
            .HasForeignKey(x => x.ProductVariantId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
    }
}
