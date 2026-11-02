using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

// Reviewed WEB04 SQL only. No product/menu creation or historical source relabeling.
public static class StorefrontCheckoutSchemaSqlCatalog
{
    public const string MigrationId = "20261031180000_AddStorefrontCheckout";
    public const string ReservationTriggerName = "TR_InventoryReservations_StorefrontSource";
    public const string LinkTriggerName = "TR_StorefrontReservationLinks_Source";
    public const string OrderSealTriggerName = "TR_StorefrontOrders_CheckoutSnapshot";
    public const string EventTriggerName = "TR_StorefrontEvents_CheckoutPlaced";
    public const string PermissionSql = "INSERT dbo.Permissions (Code,Name,GroupName) SELECT N'storefront.orders.view',N'Xem đơn đặt website',N'Storefront' WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions WITH (UPDLOCK,HOLDLOCK) WHERE Code=N'storefront.orders.view');";
    public const string PreflightSql = "IF EXISTS (SELECT 1 FROM InventoryReservations WHERE ReferenceType=12) OR EXISTS (SELECT 1 FROM InventoryTransactions WHERE ReferenceType=12) THROW 51100, 'Unrecognized storefront source already exists; do not relabel historical holds or movements.', 1;";
    public const string DownGuardSql = "IF EXISTS (SELECT 1 FROM InventoryReservations WHERE ReferenceType=12) OR EXISTS (SELECT 1 FROM StorefrontOrders) OR EXISTS (SELECT 1 FROM StorefrontOrderLines WHERE RoundingAdjustment<>0 OR DATALENGTH(UnitName)>200) THROW 51101, 'Storefront checkout data exists; downgrade would remove source or money evidence.', 1;";
    public const string LegacyDeliveryConstraint = "([ReferenceType]<>11 AND [DeliveryOrderId] IS NULL AND [DeliveryOrderLineId] IS NULL AND [SourceLegalEntityId] IS NULL AND [DeliveryRevision] IS NULL AND [ReportedByUserId] IS NULL AND [ReportedAtUtc] IS NULL) OR ([ReferenceType]=11 AND [IsDeleted]=0 AND [DeliveryOrderId] IS NOT NULL AND [DeliveryOrderLineId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [DeliveryRevision] IS NOT NULL AND [DeliveryRevision]>0 AND [ReportedByUserId] IS NOT NULL AND [ReportedAtUtc] IS NOT NULL AND [ReservedQty]>0 AND [ReferenceLineId] IS NOT NULL AND [ReferenceLineId]=[DeliveryOrderLineId] AND [ReferenceId]=CONVERT(nvarchar(50),[DeliveryOrderId]) AND ([Status]=1 OR [Status]=2 OR [Status]=3))";
    public static string DeliveryConstraint => LegacyDeliveryConstraint.Replace("AND [SourceLegalEntityId] IS NULL", "AND ([SourceLegalEntityId] IS NULL OR [ReferenceType]=12)", StringComparison.Ordinal);
    public const string ReservationConstraint = "([ReferenceType]<>12 AND [StorefrontOrderId] IS NULL AND [StorefrontOrderLineId] IS NULL AND [StorefrontFulfillmentSourceId] IS NULL) OR ([ReferenceType]=12 AND [IsDeleted]=0 AND [StorefrontOrderId] IS NOT NULL AND [StorefrontOrderLineId] IS NOT NULL AND [StorefrontFulfillmentSourceId] IS NOT NULL AND [SourceLegalEntityId] IS NOT NULL AND [ReservedQty]>0 AND [ReferenceLineId]=[StorefrontOrderLineId] AND [ReferenceLineId] IS NOT NULL AND CONVERT(varbinary(max),[ReferenceId])=CONVERT(varbinary(max),CONVERT(nvarchar(50),[StorefrontOrderId])) AND DATALENGTH([ReferenceId])=DATALENGTH(CONVERT(nvarchar(50),[StorefrontOrderId])) AND [Status] IN (1,2,3))";
    public const string LegacyLinePriceConstraint = "[Discount] <= ROUND([Quantity] * [UnitPrice], 0) AND [NetAmount] = ROUND([Quantity] * [UnitPrice], 0) - [Discount]";
    public const string LinePriceConstraint = "ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment]>=0 AND [Discount]<=ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment] AND [NetAmount]=ROUND([Quantity] * [UnitPrice],0)+[RoundingAdjustment]-[Discount]";
    private static string BinaryColumns(string alias, IEnumerable<string> columns) => string.Join(',', columns.SelectMany(column => new[] {
        $"CONVERT(varbinary(max),{alias}.[{column}])", $"DATALENGTH(CONVERT(varbinary(max),{alias}.[{column}]))" }));
    public static string OrderSealSql
    {
        get
        {
            string[] columns = ["Id","StoreId","Code","GuestTokenHash","QuoteId","PaymentMethod","DeliveryMethod","RecipientName","RecipientPhone","RecipientAddress","Note","AddressSnapshotJson","GoodsAmount","ShippingCharge","TotalDue","ShippingFeeTreatment","CreatedAtUtc","CreatedBy","IsDeleted"];
            return $"CREATE TRIGGER [dbo].[{OrderSealTriggerName}] ON [dbo].[StorefrontOrders] AFTER UPDATE,DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT {BinaryColumns("d", columns)} FROM deleted d EXCEPT SELECT {BinaryColumns("i", columns)} FROM inserted i) THROW 51102, 'Storefront order recipient, price and source-policy snapshots are immutable.', 1; END;";
        }
    }
    public static string ReservationSql
    {
        get
        {
            string[] columns = ["Id","StoreId","WarehouseId","ProductVariantId","ReferenceType","ReferenceId","ReferenceLineId","SourceLegalEntityId","StorefrontOrderId","StorefrontOrderLineId","StorefrontFulfillmentSourceId","ReservedQty","ReservedAtUtc","CreatedAtUtc","CreatedBy","IsDeleted"];
            return $"""
CREATE TRIGGER [dbo].[{ReservationTriggerName}] ON [dbo].[InventoryReservations] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.Id=d.Id WHERE d.ReferenceType<>12 AND i.ReferenceType=12)
 THROW 51103, 'Historical holds cannot be relabeled as storefront reservations.', 1;
 IF EXISTS (SELECT {BinaryColumns("d", columns)} FROM deleted d WHERE d.ReferenceType=12 EXCEPT SELECT {BinaryColumns("i", columns)} FROM inserted i WHERE i.ReferenceType=12)
 THROW 51103, 'Storefront hold identity and quantity are immutable.', 1;
 IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN StorefrontOrders o ON o.StoreId=i.StoreId AND o.Id=i.StorefrontOrderId
   WHERE i.ReferenceType=12 AND (d.Id IS NULL AND (i.Status<>1 OR i.ReleasedAtUtc IS NOT NULL OR o.Phase NOT IN(0,1)) OR i.Status<>1 AND i.ReleasedAtUtc IS NULL OR d.Status<>1 AND i.Status<>d.Status))
 THROW 51103, 'Storefront reservation interval state is invalid.', 1;
 IF EXISTS (SELECT 1 FROM inserted i JOIN StorefrontOrderLines l ON l.StoreId=i.StoreId AND l.StorefrontOrderId=i.StorefrontOrderId AND l.Id=i.StorefrontOrderLineId
   WHERE i.ReferenceType=12 AND NOT EXISTS (SELECT 1 FROM OPENJSON(l.SnapshotJson,'$.allocations') WITH
     (SourceId int '$.fulfillmentSourceId', WarehouseId int '$.warehouseId', LegalEntityId int '$.legalEntityId', BaseQuantity decimal(28,8) '$.baseQuantity') a
     WHERE a.SourceId=i.StorefrontFulfillmentSourceId AND a.WarehouseId=i.WarehouseId AND a.LegalEntityId=i.SourceLegalEntityId AND a.BaseQuantity=i.ReservedQty))
 THROW 51103, 'Storefront reservation must match its sealed source allocation.', 1;
 IF EXISTS (SELECT 1 FROM StorefrontOrderLines l JOIN (SELECT DISTINCT StoreId,StorefrontOrderId,StorefrontOrderLineId FROM inserted WHERE ReferenceType=12) touched
   ON touched.StoreId=l.StoreId AND touched.StorefrontOrderId=l.StorefrontOrderId AND touched.StorefrontOrderLineId=l.Id
   CROSS APPLY (SELECT SUM(r.ReservedQty) Held FROM InventoryReservations r WHERE r.StoreId=l.StoreId AND r.StorefrontOrderId=l.StorefrontOrderId AND r.StorefrontOrderLineId=l.Id AND r.ReferenceType=12 AND r.IsDeleted=0) b
   WHERE b.Held>l.BaseQuantity)
 THROW 51103, 'Storefront reservations exceed the immutable line budget.', 1;
END;
""";
        }
    }
    public const string LinkSql = """
CREATE TRIGGER [dbo].[TR_StorefrontReservationLinks_Source] ON [dbo].[StorefrontReservationLinks] AFTER INSERT,UPDATE,DELETE AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM deleted) THROW 51104, 'Storefront reservation links are immutable.', 1;
 IF EXISTS (SELECT 1 FROM inserted i JOIN InventoryReservations r ON r.StoreId=i.StoreId AND r.Id=i.InventoryReservationId
   WHERE r.ReferenceType<>12 OR r.StorefrontOrderId<>i.StorefrontOrderId OR r.StorefrontOrderLineId<>i.OrderLineId)
 THROW 51104, 'Storefront reservation link must reference its own typed order and line.', 1;
END;
""";
    public const string EventSql = """
CREATE TRIGGER [dbo].[TR_StorefrontEvents_CheckoutPlaced] ON [dbo].[StorefrontEvents] AFTER INSERT AS BEGIN
 SET NOCOUNT ON;
 IF EXISTS (SELECT 1 FROM inserted i JOIN StorefrontOrders o ON o.StoreId=i.StoreId AND o.Id=i.StorefrontOrderId WHERE i.Kind=N'OrderCreated' AND
   (o.Phase<>1 OR o.PaymentMethod<>0 OR o.PaymentState<>0 OR i.Revision<>1 OR NOT EXISTS(SELECT 1 FROM StorefrontOrderLines l WHERE l.StoreId=o.StoreId AND l.StorefrontOrderId=o.Id) OR
    o.GoodsAmount<>(SELECT SUM(l.NetAmount) FROM StorefrontOrderLines l WHERE l.StoreId=o.StoreId AND l.StorefrontOrderId=o.Id) OR
    EXISTS(SELECT 1 FROM StorefrontOrderLines l WHERE l.StoreId=o.StoreId AND l.StorefrontOrderId=o.Id AND l.BaseQuantity<>
       ISNULL((SELECT SUM(r.ReservedQty) FROM InventoryReservations r WHERE r.StoreId=l.StoreId AND r.StorefrontOrderId=l.StorefrontOrderId AND r.StorefrontOrderLineId=l.Id AND r.ReferenceType=12 AND r.Status=1 AND r.IsDeleted=0),0)) OR
    EXISTS(SELECT 1 FROM InventoryReservations r WHERE r.StoreId=o.StoreId AND r.StorefrontOrderId=o.Id AND r.ReferenceType=12 AND
       NOT EXISTS(SELECT 1 FROM StorefrontReservationLinks k WHERE k.StoreId=r.StoreId AND k.StorefrontOrderId=o.Id AND k.OrderLineId=r.StorefrontOrderLineId AND k.InventoryReservationId=r.Id))))
 THROW 51105, 'COD order-created proof requires complete immutable prices and typed reservations.', 1;
 IF EXISTS(SELECT 1 FROM InventoryBalances b JOIN InventoryReservations own ON own.StoreId=b.StoreId AND own.WarehouseId=b.WarehouseId AND own.ProductVariantId=b.ProductVariantId
   JOIN inserted i ON i.StoreId=own.StoreId AND i.StorefrontOrderId=own.StorefrontOrderId WHERE i.Kind=N'OrderCreated' AND own.ReferenceType=12 AND
   b.ReservedQty<>ISNULL((SELECT SUM(r.ReservedQty) FROM InventoryReservations r WHERE r.StoreId=b.StoreId AND r.WarehouseId=b.WarehouseId AND r.ProductVariantId=b.ProductVariantId AND r.Status=1 AND r.IsDeleted=0),0))
 THROW 51105, 'Shared reserved counter must equal active reservation evidence.', 1;
END;
""";
    public static IReadOnlyList<(string Table, string Name, string Sql)> Triggers => [
        ("InventoryReservations", ReservationTriggerName, ReservationSql), ("StorefrontReservationLinks", LinkTriggerName, LinkSql),
        ("StorefrontOrders", OrderSealTriggerName, OrderSealSql), ("StorefrontEvents", EventTriggerName, EventSql)];
    internal static bool TryGetTrigger(string migrationId, SqlOperation operation, out (string Table, DatabaseTriggerSchema Trigger) result)
    {
        result = default;
        if (migrationId != MigrationId || operation.SuppressTransaction) return false;
        foreach (var trigger in Triggers)
            if (operation.Sql == trigger.Sql)
            {
                result = (trigger.Table, new(DatabaseSchemaNormalization.NormalizeIdentifier(trigger.Name), DeliverySchemaSqlCatalog.NormalizeDefinition(trigger.Sql), false, false, true, true));
                return true;
            }
        return false;
    }
}
