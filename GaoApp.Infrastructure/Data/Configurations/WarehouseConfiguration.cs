using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.Property(x => x.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Location)
            .HasMaxLength(255);

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.HasIndex(x => new { x.StoreId, x.Code })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.Name })
            .IsUnique();
    }
}