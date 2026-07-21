using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftConfiguration : IEntityTypeConfiguration<POSShift>
{
    public void Configure(EntityTypeBuilder<POSShift> b)
    {
        b.ToTable("POSShifts", tb =>
        {
            tb.HasCheckConstraint("CK_POSShifts_OpeningCash_NonNegative", "[OpeningCash] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_CashSalesTotal_NonNegative", "[CashSalesTotal] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_NonCashSalesTotal_NonNegative", "[NonCashSalesTotal] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_CashInTotal_NonNegative", "[CashInTotal] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_CashOutTotal_NonNegative", "[CashOutTotal] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_ClosingCashExpected_NonNegative", "[ClosingCashExpected] >= 0");
            tb.HasCheckConstraint("CK_POSShifts_ClosingCashActual_NonNegative", "[ClosingCashActual] IS NULL OR [ClosingCashActual] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.Property(x => x.StoreId)
            .IsRequired();

        b.HasIndex(x => new { x.StoreId, x.Status });
        b.HasIndex(x => new { x.StoreId, x.OpenedAtUtc });
        b.HasIndex(x => new { x.StoreId, x.ClosedAtUtc });
        // =====================================================
        // CHỐT AN TOÀN DB:
        // Mỗi Store + Terminal chỉ được có 1 ca đang Open.
        // Service đã chặn, nhưng DB vẫn cần khóa cứng để chống race condition.
        // Status = 1 tương ứng POSShiftStatus.Open.
        // =====================================================
        b.HasIndex(x => new { x.StoreId, x.TerminalId, x.Status })
            .IsUnique()
            .HasFilter("[Status] = 1 AND [IsDeleted] = 0");
        b.Property(x => x.ShiftCode)
            .HasMaxLength(30);

        b.HasIndex(x => new { x.StoreId, x.ShiftCode })
            .IsUnique()
            .HasFilter("[ShiftCode] IS NOT NULL");

        b.Property(x => x.OpenedByUserId)
            .IsRequired();

        b.Property(x => x.OpenedAtUtc)
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.OpeningCash)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.CashSalesTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.NonCashSalesTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        // Refund totals theo ca là amount => 18,2
        b.Property(x => x.CashRefundTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.NonCashRefundTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.CashInTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.CashOutTotal)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.ClosingCashExpected)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.ClosingCashActual)
            .HasPrecision(18, 2);

        b.Property(x => x.OpenNote)
            .HasMaxLength(300);

        b.Property(x => x.CloseNote)
            .HasMaxLength(300);

        b.Ignore(x => x.IsClosed);
        b.Ignore(x => x.CashDifference);
        // =====================================================
        // FK Terminal:
        // Một ca POS bắt buộc thuộc một terminal.
        // Không cho xóa terminal nếu đã có ca phát sinh.
        // =====================================================
        b.HasOne(x => x.Terminal)
            .WithMany()
            .HasForeignKey(x => x.TerminalId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.CashTransactions)
            .WithOne(x => x.POSShift)
            .HasForeignKey(x => x.POSShiftId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Warehouse)
          .WithMany(x => x.POSShifts)
          .HasForeignKey(x => x.WarehouseId)
          .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CurrentOrder)
    .WithMany()
    .HasForeignKey(x => x.CurrentOrderId)
    .OnDelete(DeleteBehavior.Restrict);
    }
}