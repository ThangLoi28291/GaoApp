using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

// Explicit SQL contract: changing a migration body requires updating and verifying
// this catalog. Never accept arbitrary DDL or treat triggers as data-only SQL.
internal static class DeliverySchemaSqlCatalog
{
    internal const string SourceShiftValidationSql = "IF EXISTS (SELECT 1 FROM DeliveryOrders g WITH (UPDLOCK, HOLDLOCK, INDEX([IX_DeliveryOrders_StoreId_SourceCartId])) LEFT JOIN Orders o WITH (UPDLOCK, HOLDLOCK, INDEX([AK_Orders_StoreId_Id])) ON o.StoreId=g.StoreId AND o.Id=g.SourceCartId WHERE o.Id IS NULL OR o.POSShiftId<>g.CreatedShiftId) THROW 51005, 'Existing delivery source cart must belong to its created shift.', 1;";

    private sealed record Entry(string MigrationId, string Table, string Name, string Sql);
    private static readonly Entry[] Entries =
    [
        new("20261006153000_AddDeliveryFoundation", "DeliveryRevisions", "TR_DeliveryRevisions_Immutable", "CREATE TRIGGER [TR_DeliveryRevisions_Immutable] ON [DeliveryRevisions] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryJournalEntries", "TR_DeliveryJournalEntries_Immutable", "CREATE TRIGGER [TR_DeliveryJournalEntries_Immutable] ON [DeliveryJournalEntries] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryDispatchCostFragments", "TR_DeliveryDispatchCostFragments_Immutable", "CREATE TRIGGER [TR_DeliveryDispatchCostFragments_Immutable] ON [DeliveryDispatchCostFragments] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryCommandReceipts", "TR_DeliveryCommandReceipts_Immutable", "CREATE TRIGGER [TR_DeliveryCommandReceipts_Immutable] ON [DeliveryCommandReceipts] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryOutboxMessages", "TR_DeliveryOutboxMessages_Immutable", "CREATE TRIGGER [TR_DeliveryOutboxMessages_Immutable] ON [DeliveryOutboxMessages] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryOutboxReceipts", "TR_DeliveryOutboxReceipts_Immutable", "CREATE TRIGGER [TR_DeliveryOutboxReceipts_Immutable] ON [DeliveryOutboxReceipts] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;"),
        new("20261006153000_AddDeliveryFoundation", "DeliveryOrders", "TR_DeliveryOrders_Origin", "CREATE TRIGGER [TR_DeliveryOrders_Origin] ON [DeliveryOrders] AFTER UPDATE AS BEGIN IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE i.[StoreId]<>d.[StoreId] OR i.[Code]<>d.[Code] OR i.[LookupToken]<>d.[LookupToken] OR i.[SourceWarehouseId]<>d.[SourceWarehouseId] OR i.[SourceLegalEntityId]<>d.[SourceLegalEntityId] OR i.[SourceCartId]<>d.[SourceCartId] OR i.[CreatedTerminalId]<>d.[CreatedTerminalId] OR i.[CreatedShiftId]<>d.[CreatedShiftId] OR i.[CreatedByUserId]<>d.[CreatedByUserId] OR ISNULL(i.CustomerId,0)<>ISNULL(d.CustomerId,0)) THROW 51003, 'Delivery origin is immutable.', 1; END;"),
        new("20261006163000_ProtectDeliverySourceCarts", "Orders", "TR_Orders_DeliverySource", "CREATE TRIGGER [TR_Orders_DeliverySource] ON [Orders] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM deleted d JOIN DeliveryOrders g ON g.StoreId=d.StoreId AND g.SourceCartId=d.Id WHERE d.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;"),
        new("20261006163000_ProtectDeliverySourceCarts", "OrderLines", "TR_OrderLines_DeliverySource", "CREATE TRIGGER [TR_OrderLines_DeliverySource] ON [OrderLines] AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM (SELECT StoreId,OrderId FROM inserted UNION SELECT StoreId,OrderId FROM deleted) x JOIN Orders o ON o.StoreId=x.StoreId AND o.Id=x.OrderId JOIN DeliveryOrders g ON g.StoreId=o.StoreId AND g.SourceCartId=o.Id WHERE o.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;"),
        new("20261006163000_ProtectDeliverySourceCarts", "OrderPayments", "TR_OrderPayments_DeliverySource", "CREATE TRIGGER [TR_OrderPayments_DeliverySource] ON [OrderPayments] AFTER INSERT, UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM (SELECT StoreId,OrderId FROM inserted UNION SELECT StoreId,OrderId FROM deleted) x JOIN Orders o ON o.StoreId=x.StoreId AND o.Id=x.OrderId JOIN DeliveryOrders g ON g.StoreId=o.StoreId AND g.SourceCartId=o.Id WHERE o.Status=3) THROW 51004, 'Delivery source cart is immutable.', 1; END;"),
        new("20261006170000_PreserveHeldOrderShiftChanges", "DeliveryOrders", "TR_DeliveryOrders_SourceShift", "CREATE TRIGGER [TR_DeliveryOrders_SourceShift] ON [DeliveryOrders] AFTER INSERT, UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN Orders o WITH (UPDLOCK, HOLDLOCK, INDEX([AK_Orders_StoreId_Id])) ON o.StoreId=i.StoreId AND o.Id=i.SourceCartId WHERE o.Id IS NULL OR o.POSShiftId<>i.CreatedShiftId) THROW 51005, 'Delivery source cart must belong to its created shift.', 1; END;"),
        new("20261006170000_PreserveHeldOrderShiftChanges", "Orders", "TR_Orders_DeliverySourceShift", "CREATE TRIGGER [TR_Orders_DeliverySourceShift] ON [Orders] AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id JOIN DeliveryOrders g WITH (UPDLOCK, HOLDLOCK, INDEX([IX_DeliveryOrders_StoreId_SourceCartId])) ON g.StoreId=d.StoreId AND g.SourceCartId=d.Id WHERE i.POSShiftId<>d.POSShiftId) THROW 51006, 'Delivery source cart shift is immutable.', 1; END;"),
    ];

    internal static string NormalizeDefinition(string sql)
        => string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    internal static bool IsSourceShiftValidation(string migrationId, SqlOperation operation)
    {
        if (migrationId != "20261006170000_PreserveHeldOrderShiftChanges"
            || NormalizeDefinition(operation.Sql) != SourceShiftValidationSql) return false;
        if (operation.SuppressTransaction)
            throw new InvalidOperationException("Delivery source validation must run inside the migration transaction.");
        return true;
    }

    internal static bool TryGetTrigger(string migrationId, SqlOperation operation,
        out (string Table, DatabaseTriggerSchema Trigger) result)
    {
        result = default;
        var candidates = Entries.Where(x => x.MigrationId == migrationId).ToArray();
        if (candidates.Length == 0) return false;
        var normalized = NormalizeDefinition(operation.Sql);
        var entry = candidates.SingleOrDefault(x => x.Sql == normalized);
        if (operation.SuppressTransaction || entry is null)
            throw new InvalidOperationException("Delivery migration contains an unrecognized trigger SQL operation.");
        result = (entry.Table, new(DatabaseSchemaNormalization.NormalizeIdentifier(entry.Name),
            entry.Sql, IsDisabled: false, IsNotForReplication: false,
            UsesAnsiNulls: true, UsesQuotedIdentifier: true));
        return true;
    }
}
