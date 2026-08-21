using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InputInvoiceHeadConfiguration : IEntityTypeConfiguration<InputInvoiceHead>
{
    public void Configure(EntityTypeBuilder<InputInvoiceHead> builder)
    {
        builder.ToTable("InputInvoiceHead");

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
