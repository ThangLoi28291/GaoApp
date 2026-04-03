using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StockDocumentLineConfiguration : IEntityTypeConfiguration<StockDocumentLine>
{
    public void Configure(EntityTypeBuilder<StockDocumentLine> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UnitNameSnapshot)
            .HasMaxLength(100);

        builder.Property(x => x.ProductNameSnapshot)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.SkuSnapshot)
            .HasMaxLength(100);

        builder.Property(x => x.BarcodeSnapshot)
            .HasMaxLength(100);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.Factor)
            .HasColumnType("decimal(18,4)");

        builder.Property(x => x.Quantity)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.BaseQuantity)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.UnitCost)
            .HasColumnType("decimal(18,2)");

        builder.Property(x => x.LineTotal)
            .HasColumnType("decimal(18,2)");

        builder.HasIndex(x => new { x.StockDocumentId, x.LineNo })
            .IsUnique();

        builder.HasIndex(x => x.StockDocumentId);
        builder.HasIndex(x => x.ProductVariantId);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}