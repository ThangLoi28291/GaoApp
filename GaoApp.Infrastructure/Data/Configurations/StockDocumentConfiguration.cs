using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TotalAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.ApprovalNote)
            .HasMaxLength(1000);
        builder.Property(x => x.RevisionRequestNote)
    .HasMaxLength(1000);

        builder.HasIndex(x => x.HasRevisionRequest);
        builder.HasIndex(x => new { x.StoreId, x.DocumentNo })
            .IsUnique();

        builder.HasIndex(x => new { x.StoreId, x.Type, x.Status, x.DocumentDate });

        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.DocumentDate });
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.StockDocument)
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}