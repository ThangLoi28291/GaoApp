using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class StorefrontFulfillmentConfiguration : IEntityTypeConfiguration<StorefrontFulfillmentWork>,
    IEntityTypeConfiguration<StorefrontReservationTransfer>, IEntityTypeConfiguration<StorefrontFulfillmentEvidence>,
    IEntityTypeConfiguration<StorefrontCustodyIncident>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table, bool immutable = false) where T : BaseStoreEntity
    {
        b.ToTable(table, t => { t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted]=0");
            t.HasTrigger("TR_" + table + "_Source"); if (immutable) t.HasTrigger("TR_" + table + "_Immutable"); });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontOrder>().WithMany().HasForeignKey("StoreId", "StorefrontOrderId")
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<StorefrontFulfillmentWork> b)
    {
        Base(b, "StorefrontFulfillmentWorks");
        b.HasIndex(x => new { x.StoreId, x.FulfillmentSourceId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId }).IsUnique();
        b.HasOne<StorefrontFulfillmentSource>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LatestEvidence).WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.LatestEvidenceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.CourierUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CarrierName).HasMaxLength(200); b.Property(x => x.TrackingCode).HasMaxLength(200);
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontFulfillmentWorks_Stage", "[Stage] BETWEEN 0 AND 6"));
    }
    public void Configure(EntityTypeBuilder<StorefrontReservationTransfer> b)
    {
        Base(b, "StorefrontReservationTransfers", true);
        b.HasOne<StorefrontFulfillmentSource>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontOrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.OrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryReservation>().WithMany().HasForeignKey(x => new { x.StoreId, x.WebReservationId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryReservation>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryReservationId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.WebReservationId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DeliveryReservationId }).IsUnique();
        b.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontReservationTransfers_Qty", "[BaseQuantity]>0"));
    }
    public void Configure(EntityTypeBuilder<StorefrontFulfillmentEvidence> b)
    {
        Base(b, "StorefrontFulfillmentEvidence", true);
        b.HasAlternateKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id });
        b.HasOne<StorefrontFulfillmentSource>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Kind).HasMaxLength(60).IsRequired(); b.Property(x => x.Note).HasMaxLength(2000).IsRequired();
        b.Property(x => x.SnapshotJson).IsRequired(); b.ToTable(t => t.HasCheckConstraint("CK_StorefrontFulfillmentEvidence_Json", "ISJSON([SnapshotJson])=1"));
    }
    public void Configure(EntityTypeBuilder<StorefrontCustodyIncident> b)
    {
        Base(b, "StorefrontCustodyIncidents");
        b.HasOne<StorefrontFulfillmentEvidence>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.EvidenceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StorefrontFulfillmentEvidence>().WithMany().HasForeignKey(x => new { x.StoreId, x.StorefrontOrderId, x.ReconciledByEvidenceId })
            .HasPrincipalKey(x => new { x.StoreId, x.StorefrontOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ActualCustodySourceIdsJson).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_StorefrontCustodyIncidents_Json", "ISJSON([ActualCustodySourceIdsJson])=1"));
    }
}
