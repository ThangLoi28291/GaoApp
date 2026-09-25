using FluentAssertions;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

public sealed class InputInvoiceSingleActiveReceiptMigrationTests
{
    private const string MigrationId =
        "20260828150000_EnforceSingleActiveInputInvoicePerReceipt";

    [Fact]
    public void Approved_single_migration_is_present_once_and_snapshot_has_the_filtered_index()
    {
        var migrationDirectory = FindRepositoryPath("GaoApp.Infrastructure", "Migrations");
        Directory.GetFiles(migrationDirectory, $"{MigrationId}*.cs")
            .Should().HaveCount(2);

        var snapshot = File.ReadAllText(Path.Combine(
            migrationDirectory, "AppDbContextModelSnapshot.cs"));
        snapshot.Should().Contain(
            "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active");
        snapshot.Should().Contain("[IsDeleted] = 0");
    }

    [Fact]
    public async Task Migration_up_and_down_only_add_and_remove_the_active_receipt_index()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260827150000_AddInputInvoiceReconciliation");

        await migrator.MigrateAsync(MigrationId);
        (await IndexCountAsync(db)).Should().Be(1);

        await migrator.MigrateAsync("20260827150000_AddInputInvoiceReconciliation");
        (await IndexCountAsync(db)).Should().Be(0);
    }

    [Fact]
    public async Task Migration_fails_closed_and_preserves_historical_duplicate_active_rows()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260827150000_AddInputInvoiceReconciliation");
        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE [dbo].[StockDocumentInputInvoiceMap] NOCHECK CONSTRAINT ALL;
            INSERT INTO [dbo].[StockDocumentInputInvoiceMap]
                ([StockDocumentId], [InputInvoiceHeadId], [Note], [CreatedAtUtc],
                 [CreatedBy], [UpdatedAtUtc], [UpdatedBy], [IsDeleted],
                 [DeletedAtUtc], [DeletedBy], [StoreId])
            VALUES
                (91001, 92001, N'preflight-a', SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, 90001),
                (91001, 92002, N'preflight-b', SYSUTCDATETIME(), NULL, NULL, NULL, 0, NULL, NULL, 90001);
            """);

        var action = () => migrator.MigrateAsync(MigrationId);
        var failure = await action.Should().ThrowAsync<Exception>();
        failure.Which.ToString().Should().Contain(
            "UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active");
        (await db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM [dbo].[StockDocumentInputInvoiceMap] WHERE [StoreId] = 90001")
            .SingleAsync()).Should().Be(2);
        (await IndexCountAsync(db)).Should().Be(0);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(MigrationId);
    }

    private static Task<int> IndexCountAsync(AppDbContext db)
        => db.Database.SqlQueryRaw<int>(
            """
            SELECT COUNT(*) AS [Value]
            FROM sys.indexes
            WHERE [object_id] = OBJECT_ID(N'[dbo].[StockDocumentInputInvoiceMap]')
              AND [name] = N'UX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_Active'
            """).SingleAsync();

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found."),
            Path.Combine(segments));
    }
}
