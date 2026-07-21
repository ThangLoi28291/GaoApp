using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftCashDenominationConfiguration : IEntityTypeConfiguration<POSShiftCashDenomination>
{
    public void Configure(EntityTypeBuilder<POSShiftCashDenomination> b)
    {
        b.ToTable("POSShiftCashDenominations", tb =>
        {
            tb.HasCheckConstraint(
                "CK_POSShiftCashDenominations_DenominationValue_NonNegative",
                "[DenominationValue] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftCashDenominations_Quantity_NonNegative",
                "[Quantity] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftCashDenominations_Amount_NonNegative",
                "[Amount] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.POSShiftId)
            .IsRequired();

        b.Property(x => x.EntryType)
            .HasConversion<byte>()
            .IsRequired();

        b.Property(x => x.DenominationValue)
            .IsRequired();

        b.Property(x => x.Quantity)
            .IsRequired();

        b.Property(x => x.Amount)
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        b.Property(x => x.Note)
            .HasMaxLength(300);

        // Một ca, một loại bảng kê, một mệnh giá chỉ có 1 dòng active.
        b.HasIndex(x => new
        {
            x.StoreId,
            x.POSShiftId,
            x.EntryType,
            x.DenominationValue
        })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.POSShiftId, x.EntryType });

        b.HasOne(x => x.POSShift)
            .WithMany(x => x.CashDenominations)
            .HasForeignKey(x => x.POSShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}