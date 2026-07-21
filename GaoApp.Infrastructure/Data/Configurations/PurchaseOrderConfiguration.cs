using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Title).HasMaxLength(250);
        b.Property(x => x.SourceConversionKey).HasMaxLength(64);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.OutsideRequestReason).HasMaxLength(500);
        b.Property(x => x.WorkflowNote).HasMaxLength(1000);
        b.Property(x => x.SubtotalBeforeVat).HasPrecision(18, 2);
        b.Property(x => x.VatTotal).HasPrecision(18, 2);
        b.Property(x => x.TotalAfterVat).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.StoreId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.OrderDate });
        b.HasIndex(x => new { x.StoreId, x.SupplierId, x.OrderDate });
        b.HasIndex(x => new { x.StoreId, x.LegalEntityId, x.Status });
        b.HasIndex(x => x.SourcePurchaseRequestId);
        b.HasIndex(x => new { x.StoreId, x.SourcePurchaseRequestId, x.SourceConversionKey })
            .IsUnique()
            .HasFilter("[SourcePurchaseRequestId] IS NOT NULL AND [SourceConversionKey] IS NOT NULL");

        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ExpectedWarehouse).WithMany().HasForeignKey(x => x.ExpectedWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LegalEntity).WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne(x => x.PurchaseOrder).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Actions).WithOne(x => x.PurchaseOrder).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Receipts).WithOne(x => x.PurchaseOrder).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(250).IsRequired();
        b.Property(x => x.ItemKind).HasDefaultValue(GaoApp.Domain.Enums.PurchaseItemKind.Catalog);
        b.Property(x => x.SkuSnapshot).HasMaxLength(100);
        b.Property(x => x.UnitNameSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.TaxNameSnapshot).HasMaxLength(100);
        b.Property(x => x.ShortCloseReason).HasMaxLength(500);
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.Property(x => x.ConversionFactor).HasPrecision(18, 4);
        b.Property(x => x.OrderedQuantity).HasPrecision(18, 3);
        b.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
        b.Property(x => x.ShortClosedQuantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPriceBeforeVat).HasPrecision(18, 2);
        b.Property(x => x.TaxRate).HasPrecision(5, 2);
        b.Property(x => x.VatAmount).HasPrecision(18, 2);
        b.Property(x => x.UnitPriceAfterVat).HasPrecision(18, 2);
        b.Property(x => x.LineTotalBeforeVat).HasPrecision(18, 2);
        b.Property(x => x.LineTotalAfterVat).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.PurchaseOrderId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductVariantId);
        b.HasIndex(x => x.SourcePurchaseRequestLineId);
        b.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ProductUnitConversion).WithMany().HasForeignKey(x => x.ProductUnitConversionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Tax).WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderActionConfiguration : IEntityTypeConfiguration<PurchaseOrderAction>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderAction> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.StoreId, x.PurchaseOrderId, x.OccurredAtUtc });
        b.HasOne(x => x.StockDocument).WithMany().HasForeignKey(x => x.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchasePayableConfiguration : IEntityTypeConfiguration<PurchasePayable>
{
    public void Configure(EntityTypeBuilder<PurchasePayable> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceKey).HasMaxLength(150).IsRequired();
        b.Property(x => x.PayeeName).HasMaxLength(250);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.StoreId, x.SourceKey }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.RecognizedAtUtc });
        b.HasOne(x => x.StockDocument).WithMany(x => x.PurchasePayables).HasForeignKey(x => x.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
