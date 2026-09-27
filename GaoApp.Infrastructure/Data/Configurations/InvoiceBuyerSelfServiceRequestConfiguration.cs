using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InvoiceBuyerSelfServiceRequestConfiguration
    : IEntityTypeConfiguration<InvoiceBuyerSelfServiceRequest>
{
    public void Configure(
        EntityTypeBuilder<InvoiceBuyerSelfServiceRequest> b)
    {
        b.ToTable("InvoiceBuyerSelfServiceRequests");

        b.HasKey(x => x.Id);

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.OrderId)
            .IsRequired();

        b.Property(x => x.TokenHash)
            .HasColumnType("binary(32)")
            .IsRequired();

        b.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.HasIndex(x => x.TokenHash)
            .IsUnique();

        b.HasIndex(x => new
        {
            x.StoreId,
            x.OrderId,
            x.IsDeleted
        });

        b.HasIndex(x => new
        {
            x.StoreId,
            x.ExpiresAtUtc,
            x.IsDeleted
        });

        b.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}