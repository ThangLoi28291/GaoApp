using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> b)
    {
        b.HasKey(x => x.Id);

        b.Property(x => x.DateKey).HasMaxLength(8).IsRequired();

        // Unique theo Store + Date
        b.HasIndex(x => new { x.StoreId, x.DateKey }).IsUnique();
    }
}