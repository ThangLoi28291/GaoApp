using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public class InventoryReservationConfiguration : IEntityTypeConfiguration<InventoryReservation>
{
    public void Configure(EntityTypeBuilder<InventoryReservation> builder)
    {
        builder.ToTable("InventoryReservations", t => {
            t.HasTrigger("TR_InventoryReservations_DeliverySource");
            t.HasTrigger(StorefrontCheckoutSchemaSqlCatalog.ReservationTriggerName);
            t.HasCheckConstraint("CK_InventoryReservations_DeliverySource", StorefrontCheckoutSchemaSqlCatalog.DeliveryConstraint);
            t.HasCheckConstraint("CK_InventoryReservations_StorefrontSource", StorefrontCheckoutSchemaSqlCatalog.ReservationConstraint);
        });
        builder.HasOne<StorefrontOrderLine>().WithMany()
            .HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.StorefrontOrderLineId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id, x.VariantId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InventoryReservations_StorefrontOrderLine");
        builder.HasOne<StorefrontFulfillmentSource>().WithMany()
            .HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.StorefrontFulfillmentSourceId, x.WarehouseId, x.SourceLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_InventoryReservations_StorefrontFulfillmentSource");
        builder.HasIndex(x => new { x.StoreId, x.StorefrontOrderLineId, x.StorefrontFulfillmentSourceId }).IsUnique()
            .HasDatabaseName("UX_InventoryReservations_StorefrontLineSource").HasFilter("[ReferenceType]=12 AND [IsDeleted]=0");

        builder.Property(x => x.ReferenceId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Note)
            .HasMaxLength(500);

        builder.Property(x => x.ReleaseNote)
            .HasMaxLength(500);

        builder.Property(x => x.ReservedQty)
            .HasColumnType("decimal(19,4)")
            .HasDefaultValue(0);

        builder.Property(x => x.Status)
            .HasConversion<int>();
        builder.HasIndex(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId }).IsUnique()
            .HasDatabaseName("UX_InventoryReservations_ActiveDeliveryLine")
            .HasFilter("[ReferenceType]=11 AND [Status]=1 AND [IsDeleted]=0");
        builder.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.WarehouseId, x.SourceLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryOrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.ProductVariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id, x.VariantId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ReportedByUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ReferenceType,
            x.ReferenceId
        });

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.WarehouseId,
            x.ProductVariantId,
            x.Status
        });

        builder.HasIndex(x => new
        {
            x.StoreId,
            x.ReferenceType,
            x.ReferenceId,
            x.ReferenceLineId,
            x.WarehouseId,
            x.ProductVariantId,
            x.Status
        });

        builder.HasOne(x => x.Warehouse)
            .WithMany()
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ProductVariant)
            .WithMany()
            .HasForeignKey(x => x.ProductVariantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
