using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

internal static class DeliveryOpeningSourceSchemaSqlCatalog
{
    internal const string MigrationId = "20261008180000_AllowDeliveryOpeningCostSources";
    internal const string TableName = "InventoryCostLayers";
    internal const string TriggerName = "TR_InventoryCostLayers_BudgetSource";
    internal const string DropSql = "DROP TRIGGER [TR_InventoryCostLayers_BudgetSource];";
    internal const string DownGuardSql = "IF EXISTS (SELECT 1 FROM InventoryCostLayers l WITH (UPDLOCK, HOLDLOCK) JOIN InventoryValuationEntries v WITH (UPDLOCK, HOLDLOCK) ON v.StoreId=l.StoreId AND v.Id=l.InventoryValuationEntryId WHERE l.CostBudgetPolicy=1 AND v.EntryType=4) THROW 51042, 'Promoted Opening FIFO history requires the Opening source protection; use a forward fix.', 1;";
    internal static readonly string TriggerSql = BuildTrigger();

    private static string BuildTrigger()
    {
        const string previous = "v.EntryType<>1 OR";
        // Keep Inbound protections and every historical source/remainder predicate unchanged.
        const string opening = "(v.EntryType<>1 AND (v.EntryType<>4 OR t.TransactionType<>1 OR t.CostFinalizedAtUtc IS NULL OR t.IsProvisionalCost=1 OR v.CostSourceType=0 OR t.CostSourceType=0)) OR";
        var sql = DeliverySchemaSqlCatalog.InventoryLayerTriggerSql;
        if (sql.IndexOf(previous, StringComparison.Ordinal) < 0 ||
            sql.IndexOf(previous, StringComparison.Ordinal) != sql.LastIndexOf(previous, StringComparison.Ordinal))
            throw new InvalidOperationException("The historical FIFO source predicate must remain uniquely identifiable.");
        return sql.Replace(previous, opening, StringComparison.Ordinal);
    }

    internal static bool TryGetReplacementDrop(string migrationId, SqlOperation operation,
        out (string Table, string Name, string PreviousDefinition) result)
    {
        result = default;
        if (migrationId != MigrationId) return false;
        var normalized = DeliverySchemaSqlCatalog.NormalizeDefinition(operation.Sql);
        if (normalized != DropSql) return false;
        if (operation.SuppressTransaction) throw new InvalidOperationException("Opening protection replacement requires a migration transaction.");
        result = (TableName, DatabaseSchemaNormalization.NormalizeIdentifier(TriggerName),
            DeliverySchemaSqlCatalog.NormalizeDefinition(DeliverySchemaSqlCatalog.InventoryLayerTriggerSql));
        return true;
    }

    internal static bool TryGetTrigger(string migrationId, SqlOperation operation,
        out (string Table, DatabaseTriggerSchema Trigger) result)
    {
        result = default;
        if (migrationId != MigrationId) return false;
        if (operation.SuppressTransaction || DeliverySchemaSqlCatalog.NormalizeDefinition(operation.Sql) !=
            DeliverySchemaSqlCatalog.NormalizeDefinition(TriggerSql))
            throw new InvalidOperationException("Opening migration contains an unrecognized protection SQL operation.");
        result = (TableName, new(DatabaseSchemaNormalization.NormalizeIdentifier(TriggerName),
            DeliverySchemaSqlCatalog.NormalizeDefinition(TriggerSql), IsDisabled: false, IsNotForReplication: false,
            UsesAnsiNulls: true, UsesQuotedIdentifier: true));
        return true;
    }
}
