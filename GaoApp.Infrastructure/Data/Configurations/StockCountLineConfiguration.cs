using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// Cấu hình EF cho dòng kiểm kê kho.
/// </summary>
public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> builder)
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
            .HasMaxLength(1000);

        builder.Property(x => x.Factor)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.SystemQtyBase)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.CountedQty)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.CountedQtyBase)
            .HasColumnType("decimal(18,3)");

        builder.Property(x => x.DifferenceQtyBase)
            .HasColumnType("decimal(18,3)");

        // =========================================================
        // Cost snapshot for stock count valuation
        // =========================================================
        builder.Property(x => x.UnitCostSnapshot)
            .HasColumnType("decimal(18,6)")
            .HasDefaultValue(0);

        builder.Property(x => x.LineCostTotal)
            .HasColumnType("decimal(18,4)")
            .HasDefaultValue(0);

        builder.Property(x => x.IsProvisionalCost)
            .HasDefaultValue(false);

        builder.HasOne(x => x.ProductVariant)
            .WithMany(x => x.StockCountLines)
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Unit)
            .WithMany(x => x.StockCountLines)
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mỗi phiếu, số dòng là duy nhất
        builder.HasIndex(x => new { x.StockCountDocumentId, x.LineNo })
            .IsUnique();

        // Hỗ trợ tìm nhanh theo phiếu + variant
        builder.HasIndex(x => new { x.StockCountDocumentId, x.ProductVariantId });
    }
}