using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

/// <summary>
/// Cấu hình EF cho phiếu kiểm kê kho.
/// </summary>
public class StockCountDocumentConfiguration : IEntityTypeConfiguration<StockCountDocument>
{
    public void Configure(EntityTypeBuilder<StockCountDocument> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.StockCountDocuments)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.StockCountDocument)
            .HasForeignKey(x => x.StockCountDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index tìm nhanh theo kho + trạng thái + ngày
        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.Status, x.DocumentDate });

        // Số phiếu nên unique trong 1 store
        builder.HasIndex(x => new { x.StoreId, x.DocumentNo })
            .IsUnique();
    }
}