using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class OrderLegalEntityAllocationConfiguration
    : IEntityTypeConfiguration<OrderLegalEntityAllocation>
{
    public void Configure(EntityTypeBuilder<OrderLegalEntityAllocation> builder)
    {
        builder.ToTable("OrderLegalEntityAllocations", table =>
        {
            table.HasCheckConstraint(
                "CK_OrderLegalEntityAllocations_Quantity_Positive",
                "[Quantity] > 0 AND [BaseQuantity] > 0");
            table.HasCheckConstraint(
                "CK_OrderLegalEntityAllocations_SalePriority_Positive",
                "[SalePriority] > 0");
            table.HasCheckConstraint(
                "CK_OrderLegalEntityAllocations_Amounts_NonNegative",
                "[LineTotal] >= 0 AND [DiscountAllocated] >= 0 " +
                "AND [PromotionDiscountAllocated] >= 0 AND [ComboDiscountAllocated] >= 0 " +
                "AND [OrderDiscountAllocated] >= 0 AND [VoucherDiscountAllocated] >= 0 " +
                "AND [NetAmount] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.StoreId, x.Id });
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAllocated).HasPrecision(18, 2);
        builder.Property(x => x.PromotionDiscountAllocated).HasPrecision(18, 2);
        builder.Property(x => x.ComboDiscountAllocated).HasPrecision(18, 2);
        builder.Property(x => x.OrderDiscountAllocated).HasPrecision(18, 2);
        builder.Property(x => x.VoucherDiscountAllocated).HasPrecision(18, 2);
        builder.Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Property(x => x.AllocationSource).HasConversion<byte>();
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.StoreId, x.OrderId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.LegalEntityId, x.OrderId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.WarehouseId, x.ProductVariantId, x.IsDeleted });
        builder.HasIndex(x => new { x.StoreId, x.InventoryTransactionId })
            .IsUnique()
            .HasFilter("[InventoryTransactionId] IS NOT NULL AND [IsDeleted] = 0");
        builder.HasIndex(x => new
            {
                x.StoreId,
                x.OrderId,
                x.OrderLineId,
                x.LegalEntityId,
                x.WarehouseId
            })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasOne(x => x.Order)
            .WithMany(x => x.LegalEntityAllocations)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.OrderLine)
            .WithMany()
            .HasForeignKey(x => x.OrderLineId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductUnitConversion)
            .WithMany()
            .HasForeignKey(x => x.ProductUnitConversionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Hai composite FK này khóa cứng LegalEntity/Warehouse cùng Store.
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

        builder.HasOne(x => x.InventoryTransaction)
            .WithMany()
            .HasForeignKey(x => x.InventoryTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
