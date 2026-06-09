using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InventoryAdjustmentDocumentConfiguration
    : IEntityTypeConfiguration<InventoryAdjustmentDocument>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustmentDocument> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.ApprovalNote)
            .HasMaxLength(1000);

        builder.HasIndex(x => new { x.StoreId, x.DocumentNo })
            .IsUnique()
            .HasDatabaseName("UX_InventoryAdjustmentDocument_Store_DocumentNo");

        builder.HasIndex(x => new { x.StoreId, x.Status, x.DocumentDate })
            .HasDatabaseName("IX_InventoryAdjustmentDocument_Store_Status_Date");

        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.DocumentDate })
            .HasDatabaseName("IX_InventoryAdjustmentDocument_Store_Warehouse_Date");

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.Document)
            .HasForeignKey(x => x.InventoryAdjustmentDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}