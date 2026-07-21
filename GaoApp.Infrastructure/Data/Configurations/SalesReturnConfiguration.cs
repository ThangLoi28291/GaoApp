using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class SalesReturnConfiguration : IEntityTypeConfiguration<SalesReturn>
{
    public void Configure(EntityTypeBuilder<SalesReturn> builder)
    {
        builder.ToTable("SalesReturns");

        builder.HasKey(x => x.Id);

        // =========================================================
        // String fields
        // =========================================================
        builder.Property(x => x.ReturnNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        // =========================================================
        // Money fields
        // =========================================================
        builder.Property(x => x.ReturnSubtotal)
            .HasPrecision(18, 2);

        builder.Property(x => x.RefundTotal)
            .HasPrecision(18, 2);

        // =========================================================
        // Indexes
        // =========================================================
        builder.HasIndex(x => new { x.StoreId, x.OrderId });
        builder.HasIndex(x => new { x.StoreId, x.POSShiftId });
        builder.HasIndex(x => new { x.StoreId, x.ReturnNumber }).IsUnique();

        // =========================================================
        // Relationships
        // =========================================================
        builder.HasMany(x => x.Lines)
            .WithOne(x => x.SalesReturn)
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Payments)
            .WithOne(x => x.SalesReturn)
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}