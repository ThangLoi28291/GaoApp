using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerRewardLedgerConfiguration
    : IEntityTypeConfiguration<CustomerRewardLedger>
{
    public void Configure(EntityTypeBuilder<CustomerRewardLedger> b)
    {
        b.Property(x => x.Amount)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        b.Property(x => x.ReferenceCode)
            .HasMaxLength(100);

        b.Property(x => x.Description)
            .HasMaxLength(500);

        b.Property(x => x.Type)
            .HasConversion<int>()
            .IsRequired();

        // Không cho xóa khách làm mất lịch sử điểm.
        b.HasOne(x => x.Customer)
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SalesReturn)
            .WithMany()
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Voucher)
    .WithMany()
    .HasForeignKey(x => x.VoucherId)
    .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StoreId, x.VoucherId });

        // Tra cứu lịch sử điểm theo khách.
        b.HasIndex(x => new { x.StoreId, x.CustomerId, x.CreatedAtUtc });

        // Tra cứu phát sinh theo đơn bán.
        b.HasIndex(x => new { x.StoreId, x.OrderId });

        // Tra cứu phát sinh theo phiếu trả hàng.
        b.HasIndex(x => new { x.StoreId, x.SalesReturnId });

        // Dùng khi import/đối soát dữ liệu cũ.
        b.HasIndex(x => new { x.StoreId, x.ReferenceCode });

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}