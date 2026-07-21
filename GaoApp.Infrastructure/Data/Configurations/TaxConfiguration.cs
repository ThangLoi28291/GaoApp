using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class TaxConfiguration : IEntityTypeConfiguration<Tax>
{
    public void Configure(EntityTypeBuilder<Tax> b)
    {
        // base: Code/Name/IsActive... bạn đã config ở base thì khỏi lặp
        b.Property(x => x.Rate).HasColumnType("decimal(5,2)").IsRequired();

        // Unique per store
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.Name }).IsUnique();

        // Rate 0..100
        b.ToTable(t => t.HasCheckConstraint("CK_Taxes_Rate_0_100", "[Rate] >= 0 AND [Rate] <= 100"));
    }
}
