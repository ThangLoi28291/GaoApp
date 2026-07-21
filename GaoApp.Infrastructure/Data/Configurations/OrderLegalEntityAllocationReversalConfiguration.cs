using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderLegalEntityAllocationReversalConfiguration
    : IEntityTypeConfiguration<OrderLegalEntityAllocationReversal>
{
    public void Configure(EntityTypeBuilder<OrderLegalEntityAllocationReversal> builder)
    {
        builder.ToTable("OrderLegalEntityAllocationReversals", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderLegalEntityAllocationReversals_BaseQuantity_Positive",
                "[BaseQuantity] > 0");
            table.HasCheckConstraint(
                "CK_OrderLegalEntityAllocationReversals_FinancialAmount_NonNegative",
                "[FinancialAmount] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        builder.Property(x => x.FinancialAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReversalType).HasConversion<byte>();
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.OrderLegalEntityAllocationId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.SalesReturnId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.SourceValuationEntryId, x.IsDeleted });
        builder.HasIndex(x => new
            {
                x.StoreId,
                x.ReversalType,
                x.SalesReturnLineId,
                x.SourceValuationEntryId
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(x => x.Order)
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.OrderLegalEntityAllocation)
            .WithMany()
            .HasForeignKey(x => x.OrderLegalEntityAllocationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesReturn)
            .WithMany()
            .HasForeignKey(x => x.SalesReturnId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesReturnLine)
            .WithMany()
            .HasForeignKey(x => x.SalesReturnLineId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SourceValuationEntry)
            .WithMany()
            .HasForeignKey(x => x.SourceValuationEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InventoryTransaction)
            .WithMany()
            .HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.LegalEntity)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.LegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => new { x.StoreId, x.WarehouseId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
