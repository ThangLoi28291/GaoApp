using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class ProductBarcodeVerificationRequestConfiguration
    : IEntityTypeConfiguration<ProductBarcodeVerificationRequest>
{
    public void Configure(EntityTypeBuilder<ProductBarcodeVerificationRequest> b)
    {
        b.ToTable("ProductBarcodeVerificationRequests");

        b.HasKey(x => x.Id);

        b.Property(x => x.ProductNameSnapshot)
            .HasMaxLength(500)
            .IsRequired();

        b.Property(x => x.UnitNameSnapshot)
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.FactorSnapshot)
            .HasPrecision(18, 3);

        b.Property(x => x.SuggestedBarcode)
            .HasMaxLength(100);

        b.Property(x => x.EmployeeNote)
            .HasMaxLength(1000);

        b.Property(x => x.ManagerNote)
            .HasMaxLength(1000);

        b.Property(x => x.RequestType)
            .HasConversion<int>()
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        b.Property(x => x.RequestedAtUtc)
            .IsRequired();

        b.HasIndex(x => new
        {
            x.StoreId,
            x.ProductUnitConversionId,
            x.Status,
            x.IsDeleted
        });

        b.HasIndex(x => new
        {
            x.StoreId,
            x.SuggestedBarcode,
            x.Status,
            x.IsDeleted
        });

        b.HasIndex(x => new
        {
            x.StockDocumentId,
            x.IsDeleted
        });

        b.HasOne<Store>()
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ProductUnitConversion>()
      .WithMany()
      .HasForeignKey(x => x.ProductUnitConversionId)
      .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<StockDocument>()
            .WithMany()
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ProductVariantUnitBarcode>()
            .WithMany()
            .HasForeignKey(x => x.CreatedBarcodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}