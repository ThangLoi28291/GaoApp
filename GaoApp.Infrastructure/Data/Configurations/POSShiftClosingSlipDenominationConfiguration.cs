using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class POSShiftClosingSlipDenominationConfiguration : IEntityTypeConfiguration<POSShiftClosingSlipDenomination>
{
    public void Configure(EntityTypeBuilder<POSShiftClosingSlipDenomination> b)
    {
        b.ToTable("POSShiftClosingSlipDenominations", tb =>
        {
            tb.HasCheckConstraint(
                "CK_POSShiftClosingSlipDenominations_DenominationValue_NonNegative",
                "[DenominationValue] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftClosingSlipDenominations_Quantity_NonNegative",
                "[Quantity] >= 0");

            tb.HasCheckConstraint(
                "CK_POSShiftClosingSlipDenominations_Amount_NonNegative",
                "[Amount] >= 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.RowVersion).IsRowVersion();

        b.Property(x => x.StoreId).IsRequired();

        b.Property(x => x.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        b.HasIndex(x => new
        {
            x.StoreId,
            x.POSShiftClosingSlipId,
            x.DenominationValue
        })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        b.HasOne(x => x.POSShiftClosingSlip)
            .WithMany(x => x.Denominations)
            .HasForeignKey(x => x.POSShiftClosingSlipId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}