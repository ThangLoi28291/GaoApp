using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PromotionComboRuleConfiguration : IEntityTypeConfiguration<PromotionComboRule>
{
    public void Configure(EntityTypeBuilder<PromotionComboRule> b)
    {
        b.ToTable("PromotionComboRule");

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.RequiredQuantity)
            .HasPrecision(18, 4)
            .HasDefaultValue(1m);

        b.HasOne(x => x.Promotion)
            .WithMany(x => x.ComboRules)
            .HasForeignKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new
        {
            x.StoreId,
            x.PromotionId,
            x.ProductId,
            x.VariantId,
            x.ProductUnitConversionId,
            x.IsDeleted
        });

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}