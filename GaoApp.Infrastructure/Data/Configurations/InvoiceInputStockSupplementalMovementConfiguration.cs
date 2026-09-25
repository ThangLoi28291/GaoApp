using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InvoiceInputStockSupplementalMovementConfiguration
    : IEntityTypeConfiguration<InvoiceInputStockSupplementalMovement>
{
    public void Configure(EntityTypeBuilder<InvoiceInputStockSupplementalMovement> builder)
    {
        builder.ToTable("InvoiceInputStockSupplementalMovements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.QuantityChange)
            .HasPrecision(18, 4);

        builder.Property(x => x.LegacySourceKey)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.SourcePeriod)
            .HasMaxLength(50);

        builder.Property(x => x.LegacyInvoiceNumber).HasMaxLength(200);
        builder.Property(x => x.LegacyInvoiceSymbol).HasMaxLength(200);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasIndex(x => new { x.StoreId, x.LegacySourceKey })
    .IsUnique()
    .HasFilter("[StoreId] IS NOT NULL AND [LegacySourceKey] IS NOT NULL")
    .HasDatabaseName("UX_InvoiceInputStockSupplementalMovements_Store_LegacySourceKey");

        builder.HasIndex(x => new
            {
                x.StoreId,
                x.WarehouseId,
                x.ProductVariantId,
                x.EffectiveAtUtc
            })
            .HasDatabaseName("IX_InvoiceInputStockSupplementalMovements_Store_Warehouse_Variant_EffectiveAt");

        builder.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);

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
