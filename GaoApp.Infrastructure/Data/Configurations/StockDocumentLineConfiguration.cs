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

        builder.Property(x => x.UnitPriceBeforeVat).HasPrecision(18, 2);
        builder.Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Property(x => x.VatAmount).HasPrecision(18, 2);
        builder.Property(x => x.UnitPriceAfterVat).HasPrecision(18, 2);
        builder.Property(x => x.FreightAllocation).HasPrecision(18, 2);
        builder.Property(x => x.ShortageReason).HasMaxLength(500);
        builder.Property(x => x.TaxNameSnapshot).HasMaxLength(100);
        builder.Property(x => x.ReceiptAllocationKind)
            .HasDefaultValue(GaoApp.Domain.Enums.ReceiptAllocationKind.Direct);
        builder.Property(x => x.OutsidePoDecisionStatus)
            .HasDefaultValue(GaoApp.Domain.Enums.OutsidePoDecisionStatus.NotApplicable);
        builder.Property(x => x.OutsidePoDecisionNote).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.StockDocumentId, x.LineNo })
            .IsUnique();

        builder.HasIndex(x => x.StockDocumentId);
        builder.HasIndex(x => x.ProductVariantId);
        builder.HasIndex(x => x.PurchaseOrderLineId);
        builder.HasIndex(x => new
            {
                x.StockDocumentId,
                x.PurchaseOrderLineId,
                x.ProductUnitConversionId
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL")
            .HasDatabaseName("UX_StockDocumentLine_PoReceivingComponent");
        builder.HasIndex(x => new
            {
                x.StockDocumentId,
                x.ProductVariantId,
                x.ProductUnitConversionId
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [ReceiptAllocationKind] = 2 AND [ProductUnitConversionId] IS NOT NULL")
            .HasDatabaseName("UX_StockDocumentLine_OutsideReceivingComponent");

        builder.ToTable("StockDocumentLine", table => table.HasCheckConstraint(
            "CK_StockDocumentLine_ReceivingAllocation",
            "[ReceiptAllocationKind] = 0 AND [OutsidePoDecisionStatus] = 0 OR " +
            "[ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [OutsidePoDecisionStatus] = 0 OR " +
            "[ReceiptAllocationKind] = 2 AND [PurchaseOrderLineId] IS NULL AND " +
            "([OutsidePoDecisionStatus] = 1 OR [OutsidePoDecisionStatus] = 2 OR [OutsidePoDecisionStatus] = 3)"));

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PurchaseOrderLine)
            .WithMany()
            .HasForeignKey(x => x.PurchaseOrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Tax)
            .WithMany()
            .HasForeignKey(x => x.TaxId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
