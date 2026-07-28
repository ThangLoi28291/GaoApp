using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.RequestNumber).HasMaxLength(50).IsRequired();
        b.Property(x => x.Title).HasMaxLength(250).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.WorkflowNote).HasMaxLength(1000);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.StoreId, x.RequestNumber }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Status, x.RequestDate });
        b.HasIndex(x => new { x.StoreId, x.RequestedByUserId, x.Status });

        b.HasMany(x => x.Lines)
            .WithOne(x => x.PurchaseRequest)
            .HasForeignKey(x => x.PurchaseRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Actions)
            .WithOne(x => x.PurchaseRequest)
            .HasForeignKey(x => x.PurchaseRequestId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.PurchaseOrders)
            .WithOne(x => x.SourcePurchaseRequest)
            .HasForeignKey(x => x.SourcePurchaseRequestId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseRequestLineConfiguration : IEntityTypeConfiguration<PurchaseRequestLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestLine> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductNameSnapshot).HasMaxLength(250).IsRequired();
        b.Property(x => x.ItemKind)
            .HasDefaultValue(GaoApp.Domain.Enums.PurchaseItemKind.Catalog)
            .HasSentinel((GaoApp.Domain.Enums.PurchaseItemKind)0);
        b.Property(x => x.SkuSnapshot).HasMaxLength(100);
        b.Property(x => x.UnitNameSnapshot).HasMaxLength(100).IsRequired();
        b.Property(x => x.ConversionFactor).HasPrecision(18, 4);
        b.Property(x => x.RequestedQuantity).HasPrecision(18, 3);
        b.Property(x => x.ApprovedQuantity).HasPrecision(18, 3);
        b.Property(x => x.ConvertedQuantity).HasPrecision(18, 3);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.PurchaseRequestId, x.LineNo }).IsUnique();
        b.HasIndex(x => x.ProductVariantId);
        b.HasIndex(x => x.ProductUnitConversionId);
        b.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ProductUnitConversion).WithMany().HasForeignKey(x => x.ProductUnitConversionId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.PurchaseOrderLines)
            .WithOne(x => x.SourcePurchaseRequestLine)
            .HasForeignKey(x => x.SourcePurchaseRequestLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseRequestActionConfiguration : IEntityTypeConfiguration<PurchaseRequestAction>
{
    public void Configure(EntityTypeBuilder<PurchaseRequestAction> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => new { x.StoreId, x.PurchaseRequestId, x.OccurredAtUtc });
        b.HasOne(x => x.PurchaseOrder)
            .WithMany(x => x.PurchaseRequestActions)
            .HasForeignKey(x => x.PurchaseOrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
