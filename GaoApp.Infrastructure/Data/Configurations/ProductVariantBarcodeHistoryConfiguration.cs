using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class ProductVariantBarcodeHistoryConfiguration
    : IEntityTypeConfiguration<ProductVariantBarcodeHistory>
{
    public void Configure(EntityTypeBuilder<ProductVariantBarcodeHistory> e)
    {
        e.ToTable("ProductVariantBarcodeHistory");

        e.HasKey(x => x.Id);

        e.Property(x => x.OldBarcode)
            .HasMaxLength(64)
            .IsRequired(false);

        e.Property(x => x.NewBarcode)
            .HasMaxLength(64)
            .IsRequired(false);

        e.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired(false);

        e.Property(x => x.ChangedByUserName)
            .HasMaxLength(200)
            .IsRequired(false);

        e.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.ProductUnitConversion)
            .WithMany(x => x.BarcodeHistories)
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.OldBarcodeRecord)
            .WithMany(x => x.HistoryAsOldBarcode)
            .HasForeignKey(x => x.OldBarcodeId)
            .OnDelete(DeleteBehavior.Restrict);

        e.HasOne(x => x.NewBarcodeRecord)
            .WithMany(x => x.HistoryAsNewBarcode)
            .HasForeignKey(x => x.NewBarcodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Lấy lịch sử của 1 variant
        e.HasIndex(x => new { x.ProductVariantId, x.ChangedAtUtc })
            .HasDatabaseName("IX_ProductVariantBarcodeHistory_Variant_ChangedAtUtc");

        // Lấy lịch sử của 1 conversion
        e.HasIndex(x => new { x.ProductUnitConversionId, x.ChangedAtUtc })
            .HasDatabaseName("IX_ProductVariantBarcodeHistory_Conversion_ChangedAtUtc");

        // Lookup barcode cũ trong 1 store
        e.HasIndex(x => new { x.StoreId, x.OldBarcode })
            .HasDatabaseName("IX_ProductVariantBarcodeHistory_Store_OldBarcode");

        // Lookup barcode mới trong 1 store
        e.HasIndex(x => new { x.StoreId, x.NewBarcode })
            .HasDatabaseName("IX_ProductVariantBarcodeHistory_Store_NewBarcode");
    }
}