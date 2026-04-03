using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.Sku)
            .HasMaxLength(60)
            .IsRequired();

        b.Property(x => x.CostPrice)
            .HasPrecision(18, 2);

        b.Property(x => x.Price)
            .HasPrecision(18, 2);

        b.Property(x => x.IsActive)
            .HasDefaultValue(true);
        b.Property(x => x.ProductVariantName)
           .HasMaxLength(255);

        b.Property(x => x.ProductVariantNameNormalized)
            .HasMaxLength(255);

        // Quan hệ Product -> Variants
        b.HasOne(x => x.Product)
            .WithMany(p => p.Variants)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // QUAN TRỌNG: tắt cascade Store -> Variant
        b.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(v => v.PrimaryProductImage)
            .WithMany()
            .HasForeignKey(v => v.PrimaryProductImageId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => new { x.StoreId, x.ProductId, x.Sku })
            .IsUnique()
            .HasDatabaseName("UX_ProductVariant_Store_Product_Sku");

        b.HasIndex(x => new { x.StoreId, x.IsDeleted, x.IsActive, x.ProductVariantName })
            .HasDatabaseName("IX_ProductVariant_Store_ProductVariantName");

        b.HasIndex(x => new { x.StoreId, x.IsDeleted, x.IsActive, x.ProductVariantNameNormalized })
            .HasDatabaseName("IX_ProductVariant_Store_ProductVariantNameNormalized");

        b.HasIndex(x => new { x.StoreId, x.IsDeleted, x.IsActive, x.Sku })
            .HasDatabaseName("IX_ProductVariant_Store_Sku");
    }
}