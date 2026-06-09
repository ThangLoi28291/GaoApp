using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        b.Property(x => x.Phone)
            .HasMaxLength(30);

        b.Property(x => x.Address)
            .HasMaxLength(300);

        b.Property(x => x.Note)
            .HasMaxLength(500);

        b.Property(x => x.Code)
            .HasMaxLength(50);

        b.Property(x => x.CustomerGroup)
            .HasMaxLength(30)
            .HasDefaultValue("MEMBER")
            .IsRequired();

        b.Property(x => x.Email)
            .HasMaxLength(100);

        b.Property(x => x.TaxCode)
            .HasMaxLength(50);

        b.Property(x => x.HaveDebt)
            .HasDefaultValue(false);

        b.Property(x => x.IsImportedFromOldSystem)
            .HasDefaultValue(false);

        b.Property(x => x.ImportedRewardAmount)
            .HasColumnType("decimal(18,2)")
            .HasDefaultValue(0m);

        b.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Tìm khách theo số điện thoại trong từng cửa hàng
        b.HasIndex(x => new { x.StoreId, x.Phone });

        // Mã khách không bắt buộc, nhưng nếu có thì không nên trùng trong cùng cửa hàng
        b.HasIndex(x => new { x.StoreId, x.Code });

        // Dùng khi import / đối soát khách từ hệ thống cũ
        b.HasIndex(x => new { x.StoreId, x.OldCustomerId });

        // Tìm theo MST nếu có
        b.HasIndex(x => new { x.StoreId, x.TaxCode });

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}