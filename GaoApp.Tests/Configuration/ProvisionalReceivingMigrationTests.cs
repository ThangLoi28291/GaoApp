using FluentAssertions;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProvisionalReceivingMigrationTests
{
    [Fact]
    public async Task Migration_applies_additive_schema_on_isolated_sql_server_database()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables WHERE [name] = N'StockDocumentProvisionalItems';
            """)).Should().Be(1);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.check_constraints
            WHERE [name] IN (N'CK_StockDocumentProvisionalItem_Quantity',
                             N'CK_StockDocumentProvisionalItem_State',
                             N'CK_PurchaseReceivingActions_Target');
            """)).Should().Be(3);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.indexes
            WHERE [name] IN (N'UX_StockDocumentProvisionalItems_Barcode_Unit',
                             N'UX_StockDocumentProvisionalItems_Barcode_UnitSnapshot',
                             N'UX_StockDocument_EditablePurchaseReceipt');
            """)).Should().Be(3);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM sys.computed_columns c
            INNER JOIN sys.tables t ON t.[object_id] = c.[object_id]
            WHERE t.[name] = N'StockDocument'
              AND c.[name] = N'EditablePurchaseReceiptKey'
              AND c.[is_persisted] = 1;
            """)).Should().Be(1);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.foreign_keys
            WHERE [name] IN (
                N'FK_PurchaseReceivingActions_StockDocumentProvisionalItems_StockDocumentProvisionalItemId',
                N'FK_PurchaseReceiptAuditEvents_StockDocumentProvisionalItems_StockDocumentProvisionalItemId');
            """)).Should().Be(2);
    }

    [Fact]
    public void Migration_has_no_backfill_cleanup_or_auto_remediation()
    {
        var root = FindRoot();
        var path = Directory.GetFiles(Path.Combine(root, "GaoApp.Infrastructure", "Migrations"),
            "*AddProvisionalReceivingItems.cs").Single(x => !x.EndsWith(".Designer.cs"));
        var source = File.ReadAllText(path);

        source.Should().Contain("name: \"StockDocumentProvisionalItems\"");
        source.Should().Contain("EditablePurchaseReceiptKey");
        source.Should().Contain("CommandPayloadHash");
        source.Should().NotContain("UpdateData(");
        source.Should().NotContain("DeleteData(");
        source.Should().NotContain("migrationBuilder.Sql(");
        source.ToUpperInvariant().Should().NotContain("MERGE");
        source.ToUpperInvariant().Should().NotContain("DELETE FROM");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
