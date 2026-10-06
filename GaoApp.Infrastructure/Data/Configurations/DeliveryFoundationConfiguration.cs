using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GaoApp.Infrastructure.Data.Configurations;

public sealed class DeliveryFoundationConfiguration :
    IEntityTypeConfiguration<DeliveryOrder>, IEntityTypeConfiguration<DeliveryOrderLine>,
    IEntityTypeConfiguration<DeliveryRevision>, IEntityTypeConfiguration<DeliveryJournalEntry>,
    IEntityTypeConfiguration<DeliveryDispatchCostFragment>, IEntityTypeConfiguration<DeliveryCommandReceipt>,
    IEntityTypeConfiguration<DeliveryOutboxMessage>, IEntityTypeConfiguration<DeliveryOutboxReceipt>
{
    private static void Base<T>(EntityTypeBuilder<T> b, string table, bool immutable = false) where T : BaseStoreEntity
    {
        b.ToTable(table, t => {
            t.HasCheckConstraint("CK_" + table + "_Live", "[IsDeleted] = 0");
            if (immutable) t.HasTrigger("TR_" + table + "_Immutable");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");
        b.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Parent<T>(EntityTypeBuilder<T> b) where T : BaseStoreEntity
        => b.HasOne<DeliveryOrder>().WithMany().HasForeignKey("StoreId", "DeliveryOrderId")
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    private static void Json<T>(EntityTypeBuilder<T> b, string name) where T : BaseStoreEntity
    {
        b.Property<string>(name).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_" + name, "ISJSON([" + name + "]) = 1"));
    }
    private static void Source<T>(EntityTypeBuilder<T> b) where T : BaseStoreEntity
        => b.HasOne<DeliveryOrder>().WithMany().HasForeignKey("StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId")
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId }).OnDelete(DeleteBehavior.Restrict);
    private static void Cost<T>(EntityTypeBuilder<T> b) where T : BaseStoreEntity
    {
        b.Property<decimal>("BaseQuantity").HasPrecision(18, 4);
        b.Property<decimal>("UnitCost").HasPrecision(18, 6);
        b.Property<decimal>("CostAmount").HasPrecision(18, 4);
        b.ToTable(t => t.HasCheckConstraint("CK_" + b.Metadata.GetTableName() + "_Cost",
            "[BaseQuantity] >= 0 AND [UnitCost] >= 0 AND [CostAmount] >= 0"));
    }

    public void Configure(EntityTypeBuilder<DeliveryOrder> b)
    {
        Base(b, "DeliveryOrders");
        b.ToTable(t => t.HasTrigger("TR_DeliveryOrders_Origin"));
        b.HasAlternateKey(x => new { x.StoreId, x.Id });
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.SourceCartId });
        b.HasAlternateKey(x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId });
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.LookupToken).HasMaxLength(64).IsRequired();
        b.Property(x => x.RecipientName).HasMaxLength(200).IsRequired();
        b.Property(x => x.RecipientPhone).HasMaxLength(30).IsRequired();
        b.Property(x => x.RecipientAddress).HasMaxLength(1200).IsRequired();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.Property(x => x.QuotedTotal).HasPrecision(18, 2);
        b.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.LookupToken }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.SourceCartId }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.State, x.CreatedAtUtc });
        b.ToTable(t => {
            t.HasCheckConstraint("CK_DeliveryOrders_State", "[State] BETWEEN 0 AND 10 AND [Revision] > 0");
            t.HasCheckConstraint("CK_DeliveryOrders_Total", "[QuotedTotal] > 0 AND [QuotedTotal] = ROUND([QuotedTotal], 0)");
        });
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => new { x.StoreId, x.SourceWarehouseId, x.SourceLegalEntityId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.LegalEntityId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => new { x.StoreId, x.SourceCartId, x.CreatedShiftId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.POSShiftId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<POSShift>().WithMany().HasForeignKey(x => new { x.StoreId, x.CreatedShiftId, x.CreatedTerminalId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.TerminalId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.CreatedByUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Customer>().WithMany().HasForeignKey(x => new { x.StoreId, x.CustomerId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryOrderLine> b)
    {
        Base(b, "DeliveryOrderLines");
        b.HasAlternateKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id });
        b.HasOne(x => x.DeliveryOrder).WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.SourceCartId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id, x.SourceCartId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.SourceCartId, x.SourceOrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.OrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductVariant>().WithMany().HasForeignKey(x => new { x.StoreId, x.VariantId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ItemName).HasMaxLength(200).IsRequired();
        b.Property(x => x.UnitName).HasMaxLength(100).IsRequired();
        b.Property(x => x.BaseUnitName).HasMaxLength(100).IsRequired();
        b.Property(x => x.OrderedQuantity).HasPrecision(18, 4);
        b.Property(x => x.BaseMultiplier).HasPrecision(18, 6);
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        foreach (var name in new[] { "Gross", "LineDiscount", "AllocatedOrderDiscount", "Net" }) b.Property<decimal>(name).HasPrecision(18, 2);
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId, x.SourceOrderLineId }).IsUnique();
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryOrderLines_Quote",
            "[OrderedQuantity] > 0 AND [BaseMultiplier] > 0 AND [UnitPrice] >= 0 AND [Gross] >= 0 AND [LineDiscount] >= 0 AND [AllocatedOrderDiscount] >= 0 AND [Net] = [Gross]-[LineDiscount]-[AllocatedOrderDiscount] AND [Net] >= 0 AND [Net] = ROUND([Net],0)"));
    }
    public void Configure(EntityTypeBuilder<DeliveryRevision> b)
    {
        Base(b, "DeliveryRevisions", true); Parent(b);
        b.HasAlternateKey(x => new { x.StoreId, x.DeliveryOrderId, x.Revision });
        b.Property(x => x.Action).HasMaxLength(60).IsRequired();
        b.Property(x => x.AggregateVersion).HasMaxLength(16).IsRequired();
        b.Property(x => x.SnapshotHash).HasMaxLength(64).IsRequired();
        Json(b, "SnapshotJson");
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryJournalEntry> b)
    {
        Base(b, "DeliveryJournalEntries", true); Source(b); Cost(b);
        b.Property(x => x.PostingKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.MoneyAmount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.StoreId, x.PostingKey }).IsUnique();
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId }).IsUnique().HasFilter("[Kind] = 6");
        b.HasIndex(x => new { x.StoreId, x.SaleOrderId }).IsUnique().HasFilter("[Kind] = 6 AND [SaleOrderId] IS NOT NULL");
        b.HasOne<DeliveryOrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Order>().WithMany().HasForeignKey(x => new { x.StoreId, x.SaleOrderId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryJournalEntries_Money", "[Kind] BETWEEN 1 AND 7 AND [MoneyAmount] >= 0 AND [MoneyAmount] = ROUND([MoneyAmount],0)"));
    }
    public void Configure(EntityTypeBuilder<DeliveryDispatchCostFragment> b)
    {
        Base(b, "DeliveryDispatchCostFragments", true); Source(b); Cost(b);
        b.HasOne<DeliveryOrderLine>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCostLayerAllocation>().WithMany().HasForeignKey(x => new { x.StoreId, x.InventoryCostLayerAllocationId })
            .HasPrincipalKey(x => new { x.StoreId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.InventoryCostLayerAllocationId }).IsUnique();
    }
    public void Configure(EntityTypeBuilder<DeliveryCommandReceipt> b)
    {
        Base(b, "DeliveryCommandReceipts", true); Parent(b);
        b.HasIndex(x => new { x.StoreId, x.ClientRequestId }).IsUnique();
        b.Property(x => x.Operation).HasMaxLength(60).IsRequired();
        b.Property(x => x.RequestHash).HasMaxLength(64).IsRequired(); Json(b, "OutcomeJson");
        b.HasOne<UserInStore>().WithMany().HasForeignKey(x => new { x.StoreId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.StoreId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
    public void Configure(EntityTypeBuilder<DeliveryOutboxMessage> b)
    {
        Base(b, "DeliveryOutboxMessages", true);
        b.HasAlternateKey(x => new { x.StoreId, x.EventId });
        b.HasIndex(x => new { x.StoreId, x.DeliveryOrderId, x.Revision }).IsUnique();
        b.HasOne<DeliveryRevision>().WithMany().HasForeignKey(x => new { x.StoreId, x.DeliveryOrderId, x.Revision })
            .HasPrincipalKey(x => new { x.StoreId, x.DeliveryOrderId, x.Revision }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Action).HasMaxLength(60).IsRequired(); Json(b, "PayloadJson");
    }
    public void Configure(EntityTypeBuilder<DeliveryOutboxReceipt> b)
    {
        Base(b, "DeliveryOutboxReceipts", true);
        b.Property(x => x.Consumer).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.StoreId, x.EventId, x.Consumer }).IsUnique();
        b.HasOne<DeliveryOutboxMessage>().WithMany().HasForeignKey(x => new { x.StoreId, x.EventId })
            .HasPrincipalKey(x => new { x.StoreId, x.EventId }).OnDelete(DeleteBehavior.Restrict);
    }
}
