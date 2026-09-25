using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

public sealed class InputInvoiceReconciliationMigrationTests
{
    [Fact]
    public void Focused_reconciliation_migration_is_the_only_new_migration()
    {
        Assert.IsAssignableFrom<Migration>(new AddInputInvoiceReconciliation());
    }

    [Fact]
    public async Task Migration_creates_separate_overall_and_detail_snapshots_and_is_recorded_once()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();

        Assert.Equal(1, await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'StockDocumentInputInvoiceReconciliation';
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.tables
            WHERE name = N'StockDocumentInputInvoiceDetailReconciliation';
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory]
            WHERE [MigrationId] = N'20260827150000_AddInputInvoiceReconciliation';
            """));
        Assert.Equal(1, await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[StockDocumentLineInputInvoiceMap]')
              AND name = N'ExclusionReason';
            """));
        Assert.Equal(3, await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID(
                N'[dbo].[StockDocumentInputInvoiceDetailReconciliation]')
              AND name IN (N'ConfirmedUnitId', N'ConfirmedFactor',
                           N'ConfirmedBaseUnitId');
            """));
    }
}
