using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InvoiceHeadConfiguration : IEntityTypeConfiguration<InvoiceHead>
{
    public void Configure(EntityTypeBuilder<InvoiceHead> builder)
    {
        builder.ToTable("InvoiceHeads");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.InvoiceNumber)
            .HasMaxLength(50);

        builder.Property(x => x.BuyerName)
            .HasMaxLength(250);

        builder.Property(x => x.BuyerTaxCode)
            .HasMaxLength(50);

        builder.Property(x => x.BuyerAddress)
            .HasMaxLength(500);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.TotalQuantity)
            .HasPrecision(18, 3);

        builder.Property(x => x.SubTotal)
            .HasPrecision(18, 2);

        builder.Property(x => x.VatAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.GrandTotal)
            .HasPrecision(18, 2);

        builder.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Details)
            .WithOne(x => x.InvoiceHead)
            .HasForeignKey(x => x.InvoiceHeadId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.StoreId, x.OrderId })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.InvoiceDate });

        builder.HasIndex(x => new { x.StoreId, x.InvoiceNumber });
        builder.Property(x => x.IsLocked)
    .HasDefaultValue(false);

        builder.Property(x => x.LockReason)
            .HasMaxLength(500);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.IsLocked
        });
    }
}