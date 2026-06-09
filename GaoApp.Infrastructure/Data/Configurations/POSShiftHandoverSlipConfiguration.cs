using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftHandoverSlipConfiguration : IEntityTypeConfiguration<POSShiftHandoverSlip>
{
    public void Configure(EntityTypeBuilder<POSShiftHandoverSlip> b)
    {
        b.ToTable("POSShiftHandoverSlips", tb =>
        {
            tb.HasCheckConstraint(
                "CK_POSShiftHandoverSlips_OpeningCashTotal_NonNegative",
                "[OpeningCashTotal] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.SlipCode)
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.BarcodeValue)
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.OpeningCashTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.Note)
            .HasMaxLength(500);

        b.Property(x => x.CancelReason)
            .HasMaxLength(300);

        b.HasIndex(x => new { x.StoreId, x.SlipCode })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.BarcodeValue })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.Status });
        b.HasIndex(x => new { x.StoreId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.StoreId, x.WarehouseId });
        b.HasIndex(x => new { x.StoreId, x.TerminalId });

        b.HasOne(x => x.Terminal)
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.UsedPOSShift)
            .WithMany(x => x.HandoverSlips)
            .HasForeignKey(x => x.UsedPOSShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Denominations)
            .WithOne(x => x.POSShiftHandoverSlip)
            .HasForeignKey(x => x.POSShiftHandoverSlipId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}