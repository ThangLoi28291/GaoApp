using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class SalesReturnPaymentConfiguration : IEntityTypeConfiguration<SalesReturnPayment>
{
    public void Configure(EntityTypeBuilder<SalesReturnPayment> builder)
    {
        builder.ToTable("SalesReturnPayments");

        builder.HasKey(x => x.Id);

        // =========================================================
        // String fields
        // =========================================================
        builder.Property(x => x.ReferenceCode)
            .HasMaxLength(100);

        builder.Property(x => x.Provider)
            .HasMaxLength(50);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        // =========================================================
        // Money field
        // =========================================================
        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        // =========================================================
        // Indexes
        // =========================================================
        builder.HasIndex(x => new { x.StoreId, x.SalesReturnId });

        builder.HasIndex(x => new { x.StoreId, x.PaidAtUtc });

        // =========================================================
        // Relationships
        // =========================================================
        builder.HasOne(x => x.SalesReturn)
            .WithMany(x => x.Payments)
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        // =========================================================
        // Query filter
        // =========================================================
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}