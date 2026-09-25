using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class InputInvoiceSupplierResolutionEventConfiguration
    : IEntityTypeConfiguration<InputInvoiceSupplierResolutionEvent>
{
    public void Configure(
        EntityTypeBuilder<InputInvoiceSupplierResolutionEvent> builder)
    {
        builder.ToTable("InputInvoiceSupplierResolutionEvent");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.PreviousStatus).HasConversion<int>();
        builder.Property(x => x.NewStatus).HasConversion<int>();
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");

        builder.HasIndex(x => new
            { x.StoreId, x.InputInvoiceHeadId, x.CreatedAtUtc })
            .HasDatabaseName("IX_InputInvoiceSupplierResolutionEvent_Store_Invoice_Time");
        builder.HasIndex(x => new { x.StoreId, x.StockDocumentId, x.CreatedAtUtc })
            .HasDatabaseName("IX_InputInvoiceSupplierResolutionEvent_Store_Receipt_Time");

        builder.HasOne(x => x.Store)
            .WithMany()
            .HasForeignKey(x => x.StoreId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.InputInvoiceHead)
            .WithMany(x => x.SupplierResolutionEvents)
            .HasForeignKey(x => x.InputInvoiceHeadId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StockDocument)
            .WithMany()
            .HasForeignKey(x => x.StockDocumentId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.OldSupplier)
            .WithMany()
            .HasForeignKey(x => x.OldSupplierId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.NewSupplier)
            .WithMany()
            .HasForeignKey(x => x.NewSupplierId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
