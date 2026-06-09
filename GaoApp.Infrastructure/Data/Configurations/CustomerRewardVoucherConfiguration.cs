using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerRewardVoucherConfiguration
    : IEntityTypeConfiguration<CustomerRewardVoucher>
{
    public void Configure(EntityTypeBuilder<CustomerRewardVoucher> b)
    {
        b.Property(x => x.VoucherCode)
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.Value)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        b.Property(x => x.RequiredAmount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        b.Property(x => x.Status)
            .HasConversion<int>()
          
            .IsRequired();

        b.Property(x => x.Description)
            .HasMaxLength(500);

        b.Property(x => x.ReferenceCode)
            .HasMaxLength(100);

        b.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.UsedOrder)
            .WithMany()
            .HasForeignKey(x => x.UsedOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.VoucherCode })
            .IsUnique();

        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.Status });

        b.HasIndex(x => new { x.StoreId, x.ReferenceCode });

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}