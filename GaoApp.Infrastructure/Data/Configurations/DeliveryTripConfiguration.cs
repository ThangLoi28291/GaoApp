using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class DeliveryTripConfiguration : IEntityTypeConfiguration<DeliveryTrip>, IEntityTypeConfiguration<DeliveryTripOrder>,
    IEntityTypeConfiguration<DeliveryCustodyEvent>, IEntityTypeConfiguration<DeliveryCustodyLine>,
    IEntityTypeConfiguration<DeliveryTripRevision>, IEntityTypeConfiguration<DeliveryTripCommandReceipt>, IEntityTypeConfiguration<DeliveryTripOutboxMessage>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table, bool immutable = false) where T : BaseStoreEntity
    {
        b.ToTable(table, t => { t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted]=0"); if (immutable) t.HasTrigger("TR_" + table + "_Immutable"); });
        b.HasKey(x => x.Id); b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
    private static void User<T>(EntityTypeBuilder<T> b, string field) where T : BaseStoreEntity
        => b.HasOne<UserInStore>().WithMany().HasForeignKey("StoreId", field).HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    private static void Trip<T>(EntityTypeBuilder<T> b) where T : BaseStoreEntity
        => b.HasOne<DeliveryTrip>().WithMany().HasForeignKey("StoreId", "TripId").HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    private static void Json<T>(EntityTypeBuilder<T> b, string field) where T : BaseStoreEntity
    { b.Property<string>(field).IsRequired(); b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + field, "ISJSON([" + field + "])=1")); }
    public void Configure(EntityTypeBuilder<DeliveryTrip> b)
    {
        Base(b, "DeliveryTrips"); b.ToTable(t => t.HasTrigger("TR_DeliveryTrips_Transition"));
        b.HasAlternateKey(x => new { x.StoreId, x.Id }); b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.State, x.Id }); b.Property(x => x.Code).HasMaxLength(40).IsRequired(); b.Property(x => x.Note).HasMaxLength(500);
        foreach (var field in new[] { "CourierUserId", "CreatedByUserId", "HandedOverByUserId", "DepartedByUserId", "AbandonedByUserId" }) User(b, field);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryTrips_State", "[Revision]>0 AND (([State]=0 AND [HandedOverByUserId] IS NULL AND [HandedOverAtUtc] IS NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=1 AND [HandedOverByUserId] IS NOT NULL AND [HandedOverAtUtc] IS NOT NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=2 AND [HandedOverByUserId] IS NOT NULL AND [HandedOverAtUtc] IS NOT NULL AND [DepartedByUserId] IS NOT NULL AND [DepartedAtUtc] IS NOT NULL AND [DepartedAtUtc]>=[HandedOverAtUtc] AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=3 AND [HandedOverByUserId] IS NULL AND [HandedOverAtUtc] IS NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NOT NULL AND [AbandonedAtUtc] IS NOT NULL))"));
    }
    public void Configure(EntityTypeBuilder<DeliveryTripOrder> b)
    {
        Base(b, "DeliveryTripOrders"); Trip(b); b.ToTable(t => t.HasTrigger("TR_DeliveryTripOrders_Source"));
        b.HasAlternateKey(x => new { x.StoreId, x.TripId, x.Id, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId });
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId }).IsUnique().HasFilter("[IsActive]=1 AND [IsDeleted]=0");
        b.HasIndex(x => new { x.StoreId, x.TripId, x.Position }).HasFilter("[IsActive]=1 AND [IsDeleted]=0").IsUnique();
        b.Property(x => x.AssignedOrderVersion).HasMaxLength(12).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryTripOrders_Assignment", "[Position]>0 AND [AssignedApprovedRevision]>0 AND LEN([AssignedOrderVersion])=12"));
        b.HasOne<DeliveryOrder>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId }).HasPrincipalKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryCustodyEvent>().WithMany().HasForeignKey(x => new { x.StoreId, x.CurrentCustodyEventId }).HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryCustodyEvent> b)
    {
        Base(b, "DeliveryCustodyEvents", true); Trip(b); b.ToTable(t => t.HasTrigger("TR_DeliveryCustodyEvents_Source"));
        b.HasAlternateKey(x => new { x.StoreId, x.Id }); b.HasAlternateKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId });
        b.HasIndex(x => new { x.StoreId, x.EventId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId }).HasFilter("[Kind]=1").IsUnique();
        b.HasIndex(x => new { x.StoreId, x.PreviousCustodyEventId }).HasFilter("[PreviousCustodyEventId] IS NOT NULL").IsUnique();
        b.Property(x => x.ConfirmationNote).HasMaxLength(500).IsRequired(); b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<DeliveryTripOrder>().WithMany().HasForeignKey(x => new { x.StoreId, x.TripId, x.TripOrderId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId }).HasPrincipalKey(x => new { x.StoreId, x.TripId, x.Id, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryCustodyEvent>().WithMany().HasForeignKey(x => new { x.StoreId, x.PreviousCustodyEventId }).HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        foreach (var field in new[] { "CourierUserId", "PreviousCourierUserId", "ActorUserId" }) User(b, field);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryCustodyEvents_Kind", "[RootClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([ConfirmationNote])))>0 AND (([Kind]=1 AND [PreviousCustodyEventId] IS NULL AND [PreviousCourierUserId] IS NULL) OR ([Kind]=2 AND [PreviousCustodyEventId] IS NOT NULL AND [PreviousCourierUserId] IS NOT NULL AND [PreviousCourierUserId]<>[CourierUserId] AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason])))>0))"));
    }
    public void Configure(EntityTypeBuilder<DeliveryCustodyLine> b)
    {
        Base(b, "DeliveryCustodyLines", true); b.ToTable(t => t.HasTrigger("TR_DeliveryCustodyLines_Source")); b.HasIndex(x => new { x.StoreId, x.CustodyEventId, x.DeliveryOrderLineId }).IsUnique();
        b.Property(x => x.ConfirmedBaseQuantity).HasPrecision(18, 4); b.Property(x => x.CumulativeAcceptedReturnAtConfirmation).HasPrecision(18, 4);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryCustodyLines_Quantity", "[ConfirmedBaseQuantity]>=0 AND [CumulativeAcceptedReturnAtConfirmation]>=0"));
        b.HasOne<DeliveryCustodyEvent>().WithMany().HasForeignKey(x => new { x.StoreId, x.CustodyEventId, x.SourceWarehouseId, x.SourceLegalEntityId }).HasPrincipalKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        // Nullable legacy journal line/variant stay nullable: required Id FK plus joined source trigger.
        b.HasOne<DeliveryJournalEntry>().WithMany().HasForeignKey(x => x.DispatchJournalEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryOrderLine>().WithMany().HasForeignKey(x => x.DeliveryOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => new { x.StoreId, x.VariantId }).HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryTripRevision> b)
    {
        Base(b, "DeliveryTripRevisions", true); Trip(b); User(b, "ActorUserId"); b.HasAlternateKey(x => new { x.StoreId, x.TripId, x.Revision }); b.Property(x => x.Action).HasMaxLength(60).IsRequired();
        b.Property(x => x.AggregateVersion).HasMaxLength(12).IsRequired(); b.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired(); Json(b, "SnapshotJson");
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryTripRevisions_Revision", "[Revision]>0 AND LEN([AggregateVersion])=12 AND LEN([SnapshotHash])=64"));
    }
    public void Configure(EntityTypeBuilder<DeliveryTripCommandReceipt> b)
    {
        Base(b, "DeliveryTripCommandReceipts", true); Trip(b); User(b, "ActorUserId"); b.ToTable(t => t.HasTrigger("TR_DeliveryTripCommandReceipts_Namespace"));
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique(); b.Property(x => x.Operation).HasMaxLength(60).IsRequired(); b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); Json(b, "OutcomeJson");
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryTripCommandReceipts_Request", "[ClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND [RequiredCapabilities]>0 AND [RequiredCapabilities]<=131071 AND LEN([RequestHash])=64"));
    }
    public void Configure(EntityTypeBuilder<DeliveryTripOutboxMessage> b)
    {
        Base(b, "DeliveryTripOutboxMessages", true); Trip(b); b.HasIndex(x => new { x.StoreId, x.EventId }).IsUnique(); b.HasIndex(x => new { x.StoreId, x.TripId, x.Revision }).IsUnique(); b.Property(x => x.Action).HasMaxLength(60).IsRequired(); Json(b, "PayloadJson");
        b.HasOne<DeliveryTripRevision>().WithMany().HasForeignKey(x => new { x.StoreId, x.TripId, x.Revision }).HasPrincipalKey(x => new { x.StoreId, x.TripId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
    }
}
