using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PromotionItemConfiguration : IEntityTypeConfiguration<PromotionItem>
{
    public void Configure(EntityTypeBuilder<PromotionItem> b)
    {
        b.ToTable("PromotionItems");
        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Promotion)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.MinQuantity)
    .HasPrecision(18, 4)
    .HasDefaultValue(1m);

        b.HasIndex(x => new { x.StoreId, x.PromotionId });
        b.HasIndex(x => new { x.StoreId, x.ProductId });
        b.HasIndex(x => new { x.StoreId, x.VariantId });
        b.HasIndex(x => new { x.StoreId, x.ProductUnitConversionId });
        b.HasIndex(x => new
        {
            x.StoreId,
            x.PromotionId,
            x.ProductId,
            x.VariantId,
            x.ProductUnitConversionId,
            x.IsDeleted
        });

    }
}