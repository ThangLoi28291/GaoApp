using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

// Frozen WEB01 DDL definitions shared by the migration and schema inspector.
public static class StorefrontFoundationSchemaSqlCatalog
{
    public const string MigrationId = "20261030170000_AddStorefrontFoundation";
    public const string PermissionSql = """
DECLARE @Catalog TABLE (Code nvarchar(150) PRIMARY KEY, Name nvarchar(200), GroupName nvarchar(100));
INSERT @Catalog (Code, Name, GroupName) VALUES
    (N'storefront.settings.view', N'Xem cấu hình website bán hàng', N'Storefront'),
    (N'storefront.settings.manage', N'Sửa cấu hình website bán hàng', N'Storefront'),
    (N'storefront.shipping.manage', N'Quản lý vùng giao và phí website', N'Storefront'),
    (N'storefront.products.view', N'Xem sản phẩm đăng website', N'Storefront'),
    (N'storefront.products.manage', N'Quản lý sản phẩm đăng website', N'Storefront');
INSERT dbo.Permissions (Code, Name, GroupName)
SELECT source.Code, source.Name, source.GroupName FROM @Catalog AS source
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions AS target WITH (UPDLOCK, HOLDLOCK) WHERE target.Code = source.Code);
""";
    public static string[] FoundationTables { get; } =
    [
        "StorefrontSettings", "StorefrontBankCollectionAuthorities", "StorefrontProducts", "StorefrontVariants",
        "StorefrontSellingUnits", "StorefrontMedia", "StorefrontShippingZones", "StorefrontShippingRates",
        "StorefrontQuotes", "StorefrontOrders", "StorefrontOrderLines", "StorefrontPaymentIntents",
        "StorefrontBankReceipts", "StorefrontPaymentAllocations", "StorefrontFulfillmentSources", "StorefrontLineSources",
        "StorefrontReservationLinks", "StorefrontSettlementLinks", "StorefrontRefunds", "StorefrontRefundEvidence",
        "StorefrontCommandReceipts", "StorefrontEvents", "StorefrontOutboxMessages"
    ];
    public static string DowngradeGuardSql => "IF " + string.Join(" OR ", FoundationTables.Select(x => $"EXISTS (SELECT 1 FROM [dbo].[{x}])")) +
        " THROW 51091, 'Storefront data exists. Export and resolve it before downgrading; automatic data loss is prohibited.', 1;";
    public const string LineSourceGuardName = "TR_StorefrontLineSources_Source";
    public const string LineSourceGuardDefinition = """
CREATE TRIGGER [dbo].[TR_StorefrontLineSources_Source] ON [dbo].[StorefrontLineSources] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM inserted AS item
        JOIN dbo.StorefrontOrderLines AS line ON line.StoreId = item.StoreId AND line.StorefrontOrderId = item.StorefrontOrderId AND line.Id = item.OrderLineId
        JOIN dbo.StorefrontFulfillmentSources AS source ON source.StoreId = item.StoreId AND source.StorefrontOrderId = item.StorefrontOrderId AND source.Id = item.FulfillmentSourceId
        JOIN dbo.InventoryTransactions AS movement ON movement.StoreId = item.StoreId AND movement.Id = item.InventoryTransactionId
        WHERE movement.WarehouseId <> source.SourceWarehouseId OR movement.ProductVariantId <> line.VariantId
            OR movement.TransactionType <> 80 OR movement.QuantityChange >= 0
            OR movement.PostingPrecisionPolicy <> 2 OR movement.ReferenceType <> 11
            OR source.DeliveryOrderId IS NULL OR movement.ReferenceId IS NULL OR movement.ReferenceId <> CONVERT(nvarchar(50), source.DeliveryOrderId)
    ) THROW 51092, 'Storefront dispatch evidence must match the actual warehouse, SKU and outbound delivery movement.', 1;
END;
""";
    public static IReadOnlyDictionary<string, string> ProtectedTables { get; } = new Dictionary<string, string>
    {
        ["StorefrontQuotes"] = "*",
        ["StorefrontOrderLines"] = "*",
        ["StorefrontPaymentAllocations"] = "*",
        ["StorefrontRefundEvidence"] = "*",
        ["StorefrontCommandReceipts"] = "*",
        ["StorefrontEvents"] = "*",
        ["StorefrontLineSources"] = "*",
        ["StorefrontShippingRates"] = "[StoreId],[ShippingZoneId],[Revision],[ByWeight],[BaseFee],[AdditionalFee],[FirstWeightKg],[AdditionalStepKg],[FreeFromGoodsAmount]",
        ["StorefrontPaymentIntents"] = "[StoreId],[StorefrontOrderId],[Provider],[ProviderReference],[BankAccountId],[ReceivingLegalEntityId],[AmountDue],[ExpiresAtUtc],[BankTerminalCode],[BankUserCode],[BindingSnapshotJson]",
        ["StorefrontBankReceipts"] = "[StoreId],[Provider],[BankAccountId],[ReceivingLegalEntityId],[ExternalTransactionId],[EvidenceFingerprint],[ProtectedEvidence],[Amount],[OccurredAtUtc],[IsVerified]",
        ["StorefrontFulfillmentSources"] = "[StoreId],[StorefrontOrderId],[SourceWarehouseId],[SourceLegalEntityId],[DispatchPostingKey]",
        ["StorefrontOutboxMessages"] = "[StoreId],[EventId],[StorefrontOrderId],[Revision],[Kind],[PayloadJson]"
    };
    public static string TriggerName(string table) => "TR_" + table + "_Immutable";
    public static string Definition(string table)
    {
        var columns = ProtectedTables[table];
        var binaryColumns = columns == "*" ? "" : string.Join(',', columns.Split(',').SelectMany(column => new[]
            { $"CONVERT(varbinary(max),{column})", $"DATALENGTH(CONVERT(varbinary(max),{column}))" }));
        var condition = columns == "*" ? "EXISTS (SELECT 1 FROM deleted)" :
            $"EXISTS (SELECT [Id],{binaryColumns} FROM deleted EXCEPT SELECT [Id],{binaryColumns} FROM inserted)";
        if (table == "StorefrontFulfillmentSources")
            condition += " OR EXISTS (SELECT 1 FROM deleted AS previous LEFT JOIN inserted AS currentValue ON currentValue.Id = previous.Id WHERE previous.DeliveryOrderId IS NOT NULL AND (currentValue.DeliveryOrderId IS NULL OR currentValue.DeliveryOrderId <> previous.DeliveryOrderId))";
        return $"CREATE TRIGGER [dbo].[{TriggerName(table)}] ON [dbo].[{table}] AFTER UPDATE, DELETE AS BEGIN SET NOCOUNT ON; IF {condition} THROW 51090, 'Storefront evidence and source snapshots are immutable.', 1; END;";
    }
    internal static bool TryGetTrigger(string migrationId, SqlOperation operation,
        out (string Table, DatabaseTriggerSchema Trigger) result)
    {
        result = default;
        if (migrationId != MigrationId || operation.SuppressTransaction) return false;
        if (operation.Sql == LineSourceGuardDefinition)
        {
            result = ("StorefrontLineSources", new(DatabaseSchemaNormalization.NormalizeIdentifier(LineSourceGuardName), DeliverySchemaSqlCatalog.NormalizeDefinition(LineSourceGuardDefinition), false, false, true, true));
            return true;
        }
        foreach (var table in ProtectedTables.Keys)
        {
            var definition = Definition(table);
            if (operation.Sql != definition) continue;
            result = (table, new(DatabaseSchemaNormalization.NormalizeIdentifier(TriggerName(table)), DeliverySchemaSqlCatalog.NormalizeDefinition(definition), false, false, true, true));
            return true;
        }
        return false;
    }
}
