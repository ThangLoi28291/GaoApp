using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UnitNameSnapshot)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ProductNameSnapshot)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.SkuSnapshot)
            .HasMaxLength(60);

        builder.Property(x => x.BarcodeSnapshot)
            .HasMaxLength(32);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.Factor)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.Quantity)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.BaseQuantity)
            .HasColumnType("decimal(18,3)");

        // =========================================================
        // Cost snapshot for transfer valuation
        // =========================================================
        builder.Property(x => x.UnitCostSnapshot)
            .HasColumnType("decimal(18,6)")
            .HasDefaultValue(0);

        builder.Property(x => x.LineCostTotal)
            .HasColumnType("decimal(18,4)")
            .HasDefaultValue(0);

        builder.Property(x => x.IsProvisionalCost)
            .HasDefaultValue(false);

        builder.HasIndex(x => new { x.StockTransferDocumentId, x.LineNo })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId });

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}