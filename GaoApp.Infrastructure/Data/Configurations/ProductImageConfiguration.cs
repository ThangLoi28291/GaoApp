using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImages");

        b.HasKey(x => x.Id);

        b.Property(x => x.IsPrimary).IsRequired();
        b.Property(x => x.SortOrder).IsRequired();
        b.Property(x => x.AltText).HasMaxLength(200);
        b.Property(x => x.RowVersion).IsRowVersion();

        // Index phục vụ query
        b.HasIndex(x => x.ProductId);
        b.HasIndex(x => x.MediaAssetId);
        b.HasIndex(x => new { x.StoreId, x.ProductId, x.SortOrder });
        b.HasIndex(x => new { x.StoreId, x.ProductId, x.IsPrimary });

        // ✅ CHỐT: chỉ 1 ảnh primary mỗi product (filtered unique)
        b.HasIndex(x => new { x.StoreId, x.ProductId })
         .IsUnique()
          .HasFilter("[IsPrimary] = 1 AND [IsDeleted] = 0");
    }
}

