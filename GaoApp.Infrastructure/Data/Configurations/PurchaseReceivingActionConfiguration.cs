using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceivingActionConfiguration
    : IEntityTypeConfiguration<PurchaseReceivingAction>
{
    public void Configure(EntityTypeBuilder<PurchaseReceivingAction> builder)
    {
        builder.ToTable("PurchaseReceivingActions", table => table.HasCheckConstraint(
            "CK_PurchaseReceivingActions_Target",
            "[StockDocumentLineId] IS NOT NULL AND [StockDocumentProvisionalItemId] IS NULL AND [ProductVariantId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL OR " +
            "[StockDocumentLineId] IS NULL AND [StockDocumentProvisionalItemId] IS NOT NULL AND [ProductVariantId] IS NULL AND [ProductUnitConversionId] IS NULL"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BeforeQuantity).HasPrecision(18, 3);
        builder.Property(x => x.AfterQuantity).HasPrecision(18, 3);
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.CommandPayloadHash).HasMaxLength(64);
        builder.Property(x => x.BeforeProvisionalStateJson).HasMaxLength(2000);
        builder.Property(x => x.AfterProvisionalStateJson).HasMaxLength(2000);

        builder.HasIndex(x => new { x.StoreId, x.StockDocumentId, x.CommandId })
            .IsUnique()
            .HasDatabaseName("UX_PurchaseReceivingActions_Document_Command");
        builder.HasIndex(x => x.UndoOfActionId)
            .IsUnique()
            .HasFilter("[UndoOfActionId] IS NOT NULL")
            .HasDatabaseName("UX_PurchaseReceivingActions_UndoOf");
        builder.HasIndex(x => new
            {
                x.StoreId,
                x.StockDocumentId,
                x.ReceivingRevision,
                x.Id
            })
            .HasDatabaseName("IX_PurchaseReceivingActions_Latest");

        builder.HasOne(x => x.StockDocument)
            .WithMany(x => x.ReceivingActions)
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockDocumentLine)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockDocumentProvisionalItem)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentProvisionalItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.UndoOfAction)
            .WithMany()
            .HasForeignKey(x => x.UndoOfActionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
