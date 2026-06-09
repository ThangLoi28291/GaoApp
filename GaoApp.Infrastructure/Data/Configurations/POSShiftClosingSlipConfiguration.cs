using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftClosingSlipConfiguration : IEntityTypeConfiguration<POSShiftClosingSlip>
{
    public void Configure(EntityTypeBuilder<POSShiftClosingSlip> b)
    {
        b.ToTable("POSShiftClosingSlips");

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsRowVersion();

        b.Property(x => x.StoreId).IsRequired();

        b.Property(x => x.SlipCode)
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.BarcodeValue)
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.OpeningCash).HasPrecision(18, 2);
        b.Property(x => x.CashSalesTotal).HasPrecision(18, 2);
        b.Property(x => x.NonCashSalesTotal).HasPrecision(18, 2);
        b.Property(x => x.CashRefundTotal).HasPrecision(18, 2);
        b.Property(x => x.NonCashRefundTotal).HasPrecision(18, 2);
        b.Property(x => x.CashInTotal).HasPrecision(18, 2);
        b.Property(x => x.CashOutTotal).HasPrecision(18, 2);
        b.Property(x => x.ClosingCashExpected).HasPrecision(18, 2);
        b.Property(x => x.ClosingCashActual).HasPrecision(18, 2);
        b.Property(x => x.CashDifference).HasPrecision(18, 2);

        b.Property(x => x.CloseNote)
            .HasMaxLength(500);

        b.HasIndex(x => new { x.StoreId, x.SlipCode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.BarcodeValue })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // Một ca chỉ có một phiếu bàn giao active.
        b.HasIndex(x => new { x.StoreId, x.POSShiftId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasOne(x => x.POSShift)
            .WithMany(x => x.ClosingSlips)
            .HasForeignKey(x => x.POSShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}