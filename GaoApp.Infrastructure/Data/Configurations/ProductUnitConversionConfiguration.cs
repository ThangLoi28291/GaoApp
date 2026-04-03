using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class ProductUnitConversionConfiguration : IEntityTypeConfiguration<ProductUnitConversion>
{
    public void Configure(EntityTypeBuilder<ProductUnitConversion> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Factor)
            .HasPrecision(18, 4);

        builder.Property(x => x.Price)
            .HasPrecision(18, 2);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        builder.Property(x => x.IsBaseUnit)
            .HasDefaultValue(false);

        builder.Property(x => x.IsDefaultForSale)
            .HasDefaultValue(false);

        builder.Property(x => x.SortOrder)
            .HasDefaultValue(0);

        builder.HasOne(x => x.ProductVariant)
            .WithMany(x => x.UnitConversions)
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Unit)
            .WithMany(x => x.ProductUnitConversions)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mỗi variant trong 1 store không được trùng unit
        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.UnitId })
            .IsUnique();

        // Phục vụ truy vấn đơn vị mặc định / đơn vị gốc
        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.IsDefaultForSale });
        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.IsBaseUnit });
        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.IsActive });

        // Ghi chú:
        // Nếu muốn chặt hơn ở SQL Server, về sau nên tạo filtered unique index:
        // - mỗi variant chỉ có 1 IsBaseUnit = 1
        // - mỗi variant chỉ có 1 IsDefaultForSale = 1
    }
}