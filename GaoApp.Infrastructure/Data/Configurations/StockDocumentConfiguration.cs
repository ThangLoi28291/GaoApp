using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class StockDocumentConfiguration : IEntityTypeConfiguration<StockDocument>
{
    public void Configure(EntityTypeBuilder<StockDocument> builder)
    {
        builder.ToTable("StockDocument", table => table.HasCheckConstraint(
            "CK_StockDocument_ConfirmedReceiptOwner",
            "[Type] <> 1 OR [Status] <> 3 OR [ConfirmedLegalEntityId] IS NOT NULL"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TotalAmount)
            .HasColumnType("decimal(18,2)");

        builder.Property(x => x.SubtotalBeforeVat).HasPrecision(18, 2);
        builder.Property(x => x.VatAmount).HasPrecision(18, 2);
        builder.Property(x => x.FreightTotal).HasPrecision(18, 2);
        builder.Property(x => x.DirectReceiptReason).HasMaxLength(500);
        builder.Property(x => x.FreightPayeeName).HasMaxLength(250);
        builder.Property(x => x.FreightNote).HasMaxLength(1000);
        builder.Property(x => x.MerchandisePayeeName).HasMaxLength(250);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.ReceivingSessionState).HasDefaultValue(GaoApp.Domain.Enums.ReceivingSessionState.None);
        builder.Property(x => x.ReceivingRevision).HasDefaultValue(0);

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
        builder.HasIndex(x => x.PurchaseOrderId);
        builder.Property<int>("EditablePurchaseReceiptKey")
            .HasComputedColumnSql(
                "CASE WHEN [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND ([Status] = 4 OR [Status] = 1) AND [PurchaseOrderId] IS NOT NULL THEN [PurchaseOrderId] ELSE -[Id] END",
                stored: true);
        builder.HasIndex("StoreId", "EditablePurchaseReceiptKey")
            .IsUnique()
            .HasDatabaseName("UX_StockDocument_EditablePurchaseReceipt");
        builder.HasIndex(x => new { x.StoreId, x.ReceivingOwnerUserId, x.ReceivingLeaseExpiresAtUtc })
            .HasDatabaseName("IX_StockDocument_ReceivingLease");

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Supplier)
            .WithMany()
            .HasForeignKey(x => x.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.StoreId, x.ConfirmedLegalEntityId });
        builder.HasOne(x => x.ConfirmedLegalEntity)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.ConfirmedLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(x => x.StockDocument)
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
