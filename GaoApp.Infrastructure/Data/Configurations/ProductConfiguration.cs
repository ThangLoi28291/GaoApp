using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products");

        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Alias).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);

        // =========================================================
        // Money fields
        // =========================================================
        b.Property(x => x.BasePrice)
            .HasPrecision(18, 2);

        // Nếu entity có thêm:
        // b.Property(x => x.CostPrice).HasPrecision(18, 2);
        // b.Property(x => x.CompareAtPrice).HasPrecision(18, 2);

        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.StoreId, x.Alias }).IsUnique();

        b.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Brand).WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Tax).WithMany().HasForeignKey(x => x.TaxId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.BaseUnit).WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}