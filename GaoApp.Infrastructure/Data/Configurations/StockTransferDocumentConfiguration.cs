using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StockTransferDocumentConfiguration : IEntityTypeConfiguration<StockTransferDocument>
{
    public void Configure(EntityTypeBuilder<StockTransferDocument> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasIndex(x => new { x.StoreId, x.DocumentNo })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.DocumentDate, x.Status });
        builder.HasIndex(x => new { x.StoreId, x.FromWarehouseId, x.DocumentDate });
        builder.HasIndex(x => new { x.StoreId, x.ToWarehouseId, x.DocumentDate });

        builder.HasOne(x => x.FromWarehouse)
            .WithMany()
            .HasForeignKey(x => x.FromWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ToWarehouse)
            .WithMany()
            .HasForeignKey(x => x.ToWarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.StockTransferDocument)
            .HasForeignKey(x => x.StockTransferDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}