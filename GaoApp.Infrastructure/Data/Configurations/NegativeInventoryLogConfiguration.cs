using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class NegativeInventoryLogConfiguration : IEntityTypeConfiguration<NegativeInventoryLog>
{
    public void Configure(EntityTypeBuilder<NegativeInventoryLog> builder)
    {
        builder.ToTable("NegativeInventoryLog");

        builder.HasKey(x => x.Id);

        // Precision cho số lượng
        builder.Property(x => x.BeforeQty).HasPrecision(18, 3);
        builder.Property(x => x.QuantityChange).HasPrecision(18, 3);
        builder.Property(x => x.AfterQty).HasPrecision(18, 3);

        builder.Property(x => x.ReferenceId).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(500);

        // Index giúp truy vấn log âm kho nhanh hơn
        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.ProductVariantId, x.OccurredAtUtc })
            .HasDatabaseName("IX_NegativeInventoryLog_Store_Warehouse_Variant_OccurredAt");

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}