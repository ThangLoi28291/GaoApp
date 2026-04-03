using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class UnitConfig : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> b)
    {
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

        // nếu BaseStoreEntity của bạn có RowVersion + bạn muốn chuẩn rowversion:
        b.Property<byte[]>("RowVersion").IsRowVersion();
    }
}
