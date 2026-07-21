using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftHandoverSlipDenominationConfiguration : IEntityTypeConfiguration<POSShiftHandoverSlipDenomination>
{
    public void Configure(EntityTypeBuilder<POSShiftHandoverSlipDenomination> b)
    {
        b.ToTable("POSShiftHandoverSlipDenominations", tb =>
        {
            tb.HasCheckConstraint(
                "CK_POSShiftHandoverSlipDenominations_DenominationValue_NonNegative",
                "[DenominationValue] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftHandoverSlipDenominations_Quantity_NonNegative",
                "[Quantity] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftHandoverSlipDenominations_Amount_NonNegative",
                "[Amount] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion)
            .IsRowVersion();

        b.Property(x => x.StoreId)
            .IsRequired();

        b.Property(x => x.POSShiftHandoverSlipId)
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

        b.HasIndex(x => new
        {
            x.StoreId,
            x.POSShiftHandoverSlipId,
            x.DenominationValue
        })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasIndex(x => new { x.StoreId, x.POSShiftHandoverSlipId });

        b.HasOne(x => x.POSShiftHandoverSlip)
            .WithMany(x => x.Denominations)
            .HasForeignKey(x => x.POSShiftHandoverSlipId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}