using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> b)
    {
        b.ToTable("MediaAssets");

        b.HasKey(x => x.Id);

        b.Property(x => x.StoragePath)
            .IsRequired()
            .HasMaxLength(260);

        b.Property(x => x.OriginalFileName)
            .HasMaxLength(260);

        b.Property(x => x.ContentType)
            .HasMaxLength(100);

        b.Property(x => x.Sha256)
            .HasMaxLength(64);

        b.Property(x => x.SizeBytes)
            .IsRequired();

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        // Multi-tenant index (khuyến nghị)
        b.HasIndex(x => new { x.StoreId, x.Id });

        // Soft delete index (optional)
        b.HasIndex(x => new { x.StoreId, x.IsDeleted });

        // Relationship: MediaAsset 1 - N ProductImages
        b.HasMany(x => x.ProductImages)
         .WithOne(x => x.MediaAsset)
         .HasForeignKey(x => x.MediaAssetId)
         .OnDelete(DeleteBehavior.Restrict);
        // Restrict để tránh “xóa asset kéo theo xóa ảnh” ngoài ý muốn.
        // Nếu bạn muốn xóa cascade thì đổi thành Cascade.
    }
}
