using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Note).HasMaxLength(500);

        b.HasIndex(x => new { x.StoreId, x.Phone });


        // Nếu BaseEntity có RowVersion thì bạn set tại đây (tùy BaseEntity)
        // b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.HasQueryFilter(x => !x.IsDeleted);
    }
}