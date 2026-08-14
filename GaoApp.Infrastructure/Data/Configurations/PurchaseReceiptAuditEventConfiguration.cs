using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class PurchaseReceiptAuditEventConfiguration
    : IEntityTypeConfiguration<PurchaseReceiptAuditEvent>
{
    public void Configure(
        EntityTypeBuilder<PurchaseReceiptAuditEvent> builder)
    {
        builder.ToTable("PurchaseReceiptAuditEvents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).IsRequired();
        builder.Property(x => x.ActorUserName).HasMaxLength(200);
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.ChangedFieldsJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(x => x.OldValuesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(x => x.NewValuesJson)
            .HasColumnType("nvarchar(max)")
            .IsRequired();
        builder.Property(x => x.TraceId).HasMaxLength(100);
        builder.Property(x => x.IsSuccess).IsRequired();

        builder.HasIndex(x => new
            {
                x.StoreId,
                x.StockDocumentId,
                x.OccurredAtUtc,
                x.Id
            })
            .HasDatabaseName(
                "IX_PurchaseReceiptAuditEvents_Store_Document_Occurred_Id");
        builder.HasIndex(x => x.StockDocumentLineId);

        builder.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne(x => x.StockDocument)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.ClientNoAction);
        builder.HasOne(x => x.StockDocumentLine)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentLineId)
            .OnDelete(DeleteBehavior.ClientNoAction);
    }
}
