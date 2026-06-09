using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InventoryAdjustmentLineConfiguration
    : IEntityTypeConfiguration<InventoryAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentLine> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.Factor)
            .HasPrecision(18, 6);

        builder.Property(x => x.BaseQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.UnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.ProvisionalUnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.InventoryAdjustmentDocumentId
        }).HasDatabaseName("IX_InventoryAdjustmentLine_Store_Document");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ProductVariantId
        }).HasDatabaseName("IX_InventoryAdjustmentLine_Store_ProductVariant");

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}