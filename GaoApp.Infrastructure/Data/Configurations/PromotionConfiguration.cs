using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.ToTable("Promotions");
        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsRowVersion();

        b.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.Description)
            .HasMaxLength(1000);

        b.Property(x => x.Type)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.DiscountType)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.DiscountValue)
            .HasPrecision(18, 2);

        b.Property(x => x.IsActive)
            .HasDefaultValue(true);
        b.Property(x => x.CustomerPriceTier)
    .HasMaxLength(20);
        b.Property(x => x.ComboFixedPrice)
    .HasPrecision(18, 2);

        b.Property(x => x.ComboNote)
            .HasMaxLength(500);
        b.Property(x => x.BuyQuantity)
    .HasPrecision(18, 4);

        b.Property(x => x.GetQuantity)
            .HasPrecision(18, 4);

        b.Property(x => x.RequireGiftQuantityInCart)
            .HasDefaultValue(true);
        b.HasMany(x => x.Items)
            .WithOne(x => x.Promotion)
            .HasForeignKey(x => x.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.ComboRules)
    .WithOne(x => x.Promotion)
    .HasForeignKey(x => x.PromotionId)
    .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Priority)
    .HasDefaultValue(0);

        b.HasIndex(x => new { x.StoreId, x.IsActive, x.StartAtUtc, x.EndAtUtc });
        b.HasIndex(x => new { x.StoreId, x.Type, x.IsDeleted });
        b.HasIndex(x => new
        {
            x.StoreId,
            x.Type,
            x.IsActive,
            x.StartAtUtc,
            x.EndAtUtc,
            x.IsDeleted
        });
    }
}