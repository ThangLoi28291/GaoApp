using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceDetailConfiguration : IEntityTypeConfiguration<InvoiceDetail>
{
    public void Configure(EntityTypeBuilder<InvoiceDetail> builder)
    {
        builder.ToTable("InvoiceDetails");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ItemName)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.UnitName)
            .HasMaxLength(100);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.Quantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.UnitPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.VatRate)
            .HasPrecision(5, 2);

        builder.Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.SourceType)
            .HasConversion<int>()
            .IsRequired();

        builder.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.InvoiceHeadId });

        builder.HasIndex(x => new { x.StoreId, x.OrderLineId });

        builder.HasIndex(x => new { x.StoreId, x.ProductVariantId });

        builder.HasIndex(x => new { x.StoreId, x.SourceType });
    }
}