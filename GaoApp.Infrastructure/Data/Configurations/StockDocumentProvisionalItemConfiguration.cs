using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StockDocumentProvisionalItemConfiguration
    : IEntityTypeConfiguration<StockDocumentProvisionalItem>
{
    public void Configure(EntityTypeBuilder<StockDocumentProvisionalItem> builder)
    {
        builder.ToTable("StockDocumentProvisionalItems", table =>
        {
            table.HasCheckConstraint("CK_StockDocumentProvisionalItem_Quantity", "[Quantity] > 0");
            table.HasCheckConstraint("CK_StockDocumentProvisionalItem_State",
                "[Status] = 0 AND [ResolvedStockDocumentLineId] IS NULL AND [ResolutionMethod] IS NULL AND [RemovedAtUtc] IS NULL OR " +
                "[Status] = 1 AND [ResolvedStockDocumentLineId] IS NOT NULL AND [ResolvedProductVariantId] IS NOT NULL AND [ResolvedProductUnitConversionId] IS NOT NULL AND [ResolutionMethod] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL OR " +
                "[Status] = 2 AND [ResolvedStockDocumentLineId] IS NULL AND [RemovedAtUtc] IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameSnapshot).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RawBarcodeSnapshot).HasMaxLength(256);
        builder.Property(x => x.NormalizedBarcode).HasMaxLength(64);
        builder.Property(x => x.UnitNameSnapshot).HasMaxLength(100);
        builder.Property(x => x.NormalizedUnitNameSnapshot).HasMaxLength(100);
        builder.Property(x => x.Quantity).HasPrecision(18, 3);
        builder.Property(x => x.ProposedFactor).HasPrecision(18, 3);
        builder.Property(x => x.ProposedBaseUnitName).HasMaxLength(100);
        builder.HasOne(x => x.ProposedProductVariant).WithMany()
            .HasForeignKey(x => x.ProposedProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProposedBaseUnit).WithMany()
            .HasForeignKey(x => x.ProposedBaseUnitId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.PackagingPhoto).HasColumnType("varbinary(max)");
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.StoreId, x.StockDocumentId, x.Status })
            .HasDatabaseName("IX_StockDocumentProvisionalItems_Document_Status");
        builder.HasIndex(x => new { x.StockDocumentId, x.NormalizedBarcode, x.UnitId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NOT NULL")
            .HasDatabaseName("UX_StockDocumentProvisionalItems_Barcode_Unit");
        builder.HasIndex(x => new
            { x.StockDocumentId, x.NormalizedBarcode, x.NormalizedUnitNameSnapshot })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NULL AND [NormalizedUnitNameSnapshot] IS NOT NULL")
            .HasDatabaseName("UX_StockDocumentProvisionalItems_Barcode_UnitSnapshot");

        builder.HasOne(x => x.StockDocument).WithMany(x => x.ProvisionalItems)
            .HasForeignKey(x => x.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResolvedStockDocumentLine).WithMany()
            .HasForeignKey(x => x.ResolvedStockDocumentLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResolvedProductVariant).WithMany()
            .HasForeignKey(x => x.ResolvedProductVariantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ResolvedProductUnitConversion).WithMany()
            .HasForeignKey(x => x.ResolvedProductUnitConversionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedBarcode).WithMany()
            .HasForeignKey(x => x.CreatedBarcodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SupersededByProvisionalItem).WithMany()
            .HasForeignKey(x => x.SupersededByProvisionalItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
