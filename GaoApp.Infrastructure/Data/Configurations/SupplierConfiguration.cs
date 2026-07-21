using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.TaxCode).HasMaxLength(50);
        b.Property(x => x.Note).HasMaxLength(500);

        // RowVersion đúng chuẩn byte[] => rowversion/timestamp
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        // Unique theo Store
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

        // Soft delete filter (nếu bạn đã làm global filter thì bỏ dòng này)
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
