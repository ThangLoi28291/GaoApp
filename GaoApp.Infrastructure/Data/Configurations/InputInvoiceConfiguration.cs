using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InputInvoiceHeadConfiguration : IEntityTypeConfiguration<InputInvoiceHead>
{
    public void Configure(EntityTypeBuilder<InputInvoiceHead> builder)
    {
        builder.ToTable("InputInvoiceHead");

        var normalizedBuyerTaxCode = builder.Property(x => x.NormalizedBuyerTaxCode)
            .HasMaxLength(50)
            .HasComputedColumnSql(
                "CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([BuyerTaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''))",
                stored: true);
        normalizedBuyerTaxCode.ValueGeneratedNever();
        normalizedBuyerTaxCode.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        normalizedBuyerTaxCode.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(x => x.BuyerOwnerResolutionStatus)
            .HasConversion<int>()
            .HasDefaultValue(GaoApp.Domain.Enums.InputInvoiceBuyerOwnerResolutionStatus.NotEvaluated);

        builder.HasIndex(x => new { x.StoreId, x.NormalizedBuyerTaxCode });
        builder.HasIndex(x => new { x.StoreId, x.ResolvedBuyerLegalEntityId });
        builder.HasOne(x => x.ResolvedBuyerLegalEntity)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.ResolvedBuyerLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.SupplierResolutionStatus)
            .HasConversion<int>()
            .HasDefaultValue(GaoApp.Domain.Enums.InputInvoiceSupplierResolutionStatus.NotEvaluated);

        builder.HasIndex(x => new { x.StoreId, x.ResolvedSupplierId })
            .HasDatabaseName("IX_InputInvoiceHead_StoreId_ResolvedSupplierId");

        builder.HasOne(x => x.ResolvedSupplier)
            .WithMany()
            .HasForeignKey(x => x.ResolvedSupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.SellerTaxCode,
            x.InvoiceTemplateCode,
            x.InvoiceSeries,
            x.InvoiceNumber
        });

        builder.HasIndex(
            [nameof(InputInvoiceHead.StoreId), nameof(InputInvoiceHead.XmlHash)],
            "IX_InputInvoiceHead_StoreId_XmlHash");

        builder.HasIndex(
                [nameof(InputInvoiceHead.StoreId), nameof(InputInvoiceHead.XmlHash)],
                "UX_InputInvoiceHead_StoreId_XmlHash_Active")
            .HasDatabaseName(
                "UX_InputInvoiceHead_StoreId_XmlHash_Active")
            .IsUnique()
            .HasFilter("[XmlHash] IS NOT NULL AND [IsDeleted] = 0");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.NormalizedSellerTaxCode,
            x.NormalizedInvoiceSeries,
            x.NormalizedInvoiceNumber,
            x.InvoiceIdentityDate
        })
            .HasDatabaseName(
                "UX_InputInvoiceHead_StoreId_BusinessIdentity_Active")
            .IsUnique()
            .HasFilter(
                "[NormalizedSellerTaxCode] IS NOT NULL " +
                "AND [NormalizedInvoiceSeries] IS NOT NULL " +
                "AND [NormalizedInvoiceNumber] IS NOT NULL " +
                "AND [InvoiceIdentityDate] IS NOT NULL " +
                "AND [IsDeleted] = 0");

        builder.HasMany(x => x.Details)
            .WithOne(x => x.InputInvoiceHead)
            .HasForeignKey(x => x.InputInvoiceHeadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class InputInvoiceDetailConfiguration : IEntityTypeConfiguration<InputInvoiceDetail>
{
    public void Configure(EntityTypeBuilder<InputInvoiceDetail> builder)
    {
        builder.ToTable("InputInvoiceDetail");

        builder.HasIndex(x => new { x.InputInvoiceHeadId, x.LineNo });
        builder.HasIndex(x => new
        {
            x.InputInvoiceHeadId,
            x.NormalizedSupplierItemCode,
            x.NormalizedUnitName
        });
    }
}

public sealed class InputInvoiceItemCatalogMapConfiguration
    : IEntityTypeConfiguration<InputInvoiceItemCatalogMap>
{
    public void Configure(EntityTypeBuilder<InputInvoiceItemCatalogMap> builder)
    {
        builder.ToTable("InputInvoiceItemCatalogMap");
        builder.Property(x => x.ConfirmedFactor).HasPrecision(18, 4);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new
            {
                x.StoreId,
                x.SupplierId,
                x.NormalizedSupplierItemCode,
                x.NormalizedSupplierUnitName
            })
            .HasDatabaseName("UX_InputInvoiceItemCatalogMap_CodeUnit_Active")
            .IsUnique()
            .HasFilter(
                "[NormalizedSupplierItemCode] IS NOT NULL AND " +
                "[IsActive] = 1 AND [IsDeleted] = 0");

        builder.HasIndex(x => new
            {
                x.StoreId,
                x.SupplierId,
                x.NormalizedSupplierItemName,
                x.NormalizedSupplierUnitName
            })
            .HasDatabaseName("UX_InputInvoiceItemCatalogMap_NameUnit_Active")
            .IsUnique()
            .HasFilter(
                "[NormalizedSupplierItemCode] IS NULL AND " +
                "[IsActive] = 1 AND [IsDeleted] = 0");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ProductVariantId,
            x.ProductUnitConversionId,
            x.IsActive
        });

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConfirmedUnit)
            .WithMany()
            .HasForeignKey(x => x.ConfirmedUnitId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ConfirmedBaseUnit)
            .WithMany()
            .HasForeignKey(x => x.ConfirmedBaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockDocumentInputInvoiceMapConfiguration : IEntityTypeConfiguration<StockDocumentInputInvoiceMap>
{
    public void Configure(EntityTypeBuilder<StockDocumentInputInvoiceMap> builder)
    {
        builder.ToTable("StockDocumentInputInvoiceMap");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.StockDocumentId,
            x.InputInvoiceHeadId
        }).IsUnique();

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.StockDocumentId
        })
            .HasDatabaseName(StockDocumentInputInvoiceMap.ActiveReceiptIndexName)
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(x => x.StockDocument)
            .WithMany(x => x.InputInvoiceMaps)
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InputInvoiceHead)
            .WithMany(x => x.StockDocumentMaps)
            .HasForeignKey(x => x.InputInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockDocumentLineInputInvoiceMapConfiguration : IEntityTypeConfiguration<StockDocumentLineInputInvoiceMap>
{
    public void Configure(EntityTypeBuilder<StockDocumentLineInputInvoiceMap> builder)
    {
        builder.ToTable("StockDocumentLineInputInvoiceMap");

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.StockDocumentLineId
        }).IsUnique();

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.StockDocumentId,
            x.UseInputInvoice
        });

        builder.HasOne(x => x.StockDocument)
            .WithMany(x => x.LineInputInvoiceMaps)
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StockDocumentLine)
            .WithMany(x => x.InputInvoiceMaps)
            .HasForeignKey(x => x.StockDocumentLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.InputInvoiceDetail)
            .WithMany(x => x.StockDocumentLineMaps)
            .HasForeignKey(x => x.InputInvoiceDetailId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockDocumentInputInvoiceReconciliationConfiguration
    : IEntityTypeConfiguration<StockDocumentInputInvoiceReconciliation>
{
    public void Configure(EntityTypeBuilder<StockDocumentInputInvoiceReconciliation> builder)
    {
        builder.ToTable("StockDocumentInputInvoiceReconciliation");
        builder.Property(x => x.EvidenceFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AcceptedEvidenceFingerprint).HasMaxLength(64);
        builder.Property(x => x.AcceptanceReason).HasMaxLength(1000);
        builder.Property(x => x.UnsupportedHeaderReason).HasMaxLength(500);
        builder.HasIndex(x => new { x.StoreId, x.StockDocumentInputInvoiceMapId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_InputInvoiceReconciliation_Link_Active");
        builder.HasIndex(x => new { x.StoreId, x.StockDocumentId, x.OverallState })
            .HasDatabaseName("IX_InputInvoiceReconciliation_Receipt_State");
        builder.HasOne(x => x.StockDocumentInputInvoiceMap)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentInputInvoiceMapId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockDocument)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InputInvoiceHead)
            .WithMany()
            .HasForeignKey(x => x.InputInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockDocumentInputInvoiceDetailReconciliationConfiguration
    : IEntityTypeConfiguration<StockDocumentInputInvoiceDetailReconciliation>
{
    public void Configure(EntityTypeBuilder<StockDocumentInputInvoiceDetailReconciliation> builder)
    {
        builder.ToTable("StockDocumentInputInvoiceDetailReconciliation");
        builder.Property(x => x.IgnoreReason).HasMaxLength(1000);
        builder.Property(x => x.VatComparisonReason).HasMaxLength(500);
        builder.HasIndex(x => new
            {
                x.StoreId,
                x.StockDocumentInputInvoiceReconciliationId,
                x.InputInvoiceDetailId
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0")
            .HasDatabaseName("UX_InputInvoiceDetailReconciliation_Detail_Active");
        builder.HasIndex(x => new
            {
                x.StoreId,
                x.StockDocumentId,
                x.InputInvoiceHeadId,
                x.DetailState
            })
            .HasDatabaseName("IX_InputInvoiceDetailReconciliation_Receipt_State");
        builder.HasOne(x => x.Reconciliation)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.StockDocumentInputInvoiceReconciliationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InputInvoiceDetail)
            .WithMany()
            .HasForeignKey(x => x.InputInvoiceDetailId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
