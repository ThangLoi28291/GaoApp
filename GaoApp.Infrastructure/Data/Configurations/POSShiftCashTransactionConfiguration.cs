using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftCashTransactionConfiguration : IEntityTypeConfiguration<POSShiftCashTransaction>
{
    public void Configure(EntityTypeBuilder<POSShiftCashTransaction> builder)
    {
        builder.ToTable("POSShiftCashTransactions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount)
            .HasPrecision(18, 2);

        builder.Property(x => x.Reason)
            .HasMaxLength(300);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.Type)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.CreatedByUserId)
            .IsRequired();

        builder.HasOne(x => x.POSShift)
            .WithMany(x => x.CashTransactions)
            .HasForeignKey(x => x.POSShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.POSShiftId });

        builder.HasIndex(x => new { x.StoreId, x.Type });

        builder.HasIndex(x => x.CreatedAtUtc);
    }
}