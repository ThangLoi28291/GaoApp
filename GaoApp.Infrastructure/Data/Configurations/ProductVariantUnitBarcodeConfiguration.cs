using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class ProductVariantUnitBarcodeConfiguration : IEntityTypeConfiguration<ProductVariantUnitBarcode>
{
    public void Configure(EntityTypeBuilder<ProductVariantUnitBarcode> builder)
    {
        builder.ToTable("ProductVariantUnitBarcode");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Barcode)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(250);

        builder.Property(x => x.IsPrimary)
            .HasDefaultValue(true);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany(x => x.Barcodes)
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Query barcode theo conversion
        builder.HasIndex(x => new { x.StoreId, x.ProductUnitConversionId })
            .HasDatabaseName("IX_ProductVariantUnitBarcode_Store_Conversion");

        // Query barcode active/primary theo conversion
        builder.HasIndex(x => new { x.ProductUnitConversionId, x.IsActive, x.IsPrimary })
            .HasDatabaseName("IX_ProductVariantUnitBarcode_Conversion_Active_Primary");

        // Lookup barcode theo store + barcode
        // Chỉ cấm trùng giữa các record ACTIVE chưa xóa mềm
        builder.HasIndex(x => new { x.StoreId, x.Barcode })
            .HasDatabaseName("UX_ProductVariantUnitBarcode_Store_Barcode_Active")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");

        // Mỗi conversion chỉ có 1 primary active barcode
        builder.HasIndex(x => x.ProductUnitConversionId)
            .HasDatabaseName("UX_ProductVariantUnitBarcode_Conversion_Primary_Active")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [IsPrimary] = 1");
    }
}