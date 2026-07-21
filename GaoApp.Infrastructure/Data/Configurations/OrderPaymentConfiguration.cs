using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderPaymentConfiguration : IEntityTypeConfiguration<OrderPayment>
{
    public void Configure(EntityTypeBuilder<OrderPayment> b)
    {
        b.ToTable("OrderPayments");

        b.HasKey(x => x.Id);

        b.Property(x => x.ReferenceCode)
            .HasMaxLength(100);

        b.Property(x => x.Provider)
            .HasMaxLength(50);

        // =========================================================
        // Money fields
        // =========================================================
        b.Property(x => x.Amount)
            .HasPrecision(18, 2);

        // Nếu entity có các field sau thì bật lên:
        // b.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        // b.Property(x => x.ChangeAmount).HasPrecision(18, 2);
        // b.Property(x => x.FeeAmount).HasPrecision(18, 2);

        b.HasIndex(x => new { x.StoreId, x.OrderId });

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}