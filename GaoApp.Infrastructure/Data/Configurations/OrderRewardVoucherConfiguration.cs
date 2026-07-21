using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderRewardVoucherConfiguration : IEntityTypeConfiguration<OrderRewardVoucher>
{
    public void Configure(EntityTypeBuilder<OrderRewardVoucher> b)
    {
        b.ToTable("OrderRewardVouchers");

        b.HasKey(x => x.Id);

        b.Property(x => x.VoucherValue)
            .HasPrecision(18, 2);

        b.HasOne(x => x.Order)
            .WithMany(x => x.RewardVouchers)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Voucher)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.VoucherId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasIndex(x => new { x.StoreId, x.OrderId });

        b.HasIndex(x => new { x.StoreId, x.VoucherId });
    }
}