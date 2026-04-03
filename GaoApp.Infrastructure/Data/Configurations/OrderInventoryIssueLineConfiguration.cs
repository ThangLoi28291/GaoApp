using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class OrderInventoryIssueLineConfiguration : IEntityTypeConfiguration<OrderInventoryIssueLine>
{
    public void Configure(EntityTypeBuilder<OrderInventoryIssueLine> builder)
    {
        builder.ToTable("OrderInventoryIssueLines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderedQty)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(x => x.StockBefore)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(x => x.StockAfter)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(x => x.NegativeQty)
            .HasPrecision(18, 4)
            .IsRequired();

        builder.Property(x => x.ProvisionalUnitCost)
            .HasPrecision(18, 6);

        builder.Property(x => x.ProvisionalCostAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.RevaluationAmount)
            .HasPrecision(18, 2);

        // =====================================================
        // Auto-detect fields
        // =====================================================
        // Qty auto detect là quantity nghiệp vụ tồn kho => 18,4
        builder.Property(x => x.AutoDetectedInboundQty)
            .HasPrecision(18, 4)
            .HasDefaultValue(0m);

        // Revaluation auto detect là amount => 18,2
        builder.Property(x => x.AutoDetectedRevaluationAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.IsResolved)
            .IsRequired();

        builder.HasOne(x => x.OrderInventoryIssue)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.OrderInventoryIssueId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Barcode)
            .WithMany()
            .HasForeignKey(x => x.BarcodeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.OrderInventoryIssueId, x.IsDeleted });
        builder.HasIndex(x => new { x.OrderId, x.IsDeleted });
        builder.HasIndex(x => new { x.OrderLineId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId, x.IsResolved, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.ProductUnitConversionId, x.IsResolved, x.IsDeleted });
    }
}