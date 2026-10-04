using System.Data.Common;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class ImportJournalCompatibilityTests
{
    private const string Prefix = "20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService";
    private const string Journals = """
        CREATE TABLE dbo.GaoStoreMigrationRunsV2(PackageId nvarchar(100) NOT NULL PRIMARY KEY,SourceDatabase sysname NOT NULL,SqlSha256 char(64) NOT NULL,SourceHashes nvarchar(max) NOT NULL,TargetHashes nvarchar(max) NOT NULL,CommittedAtUtc datetime2 NOT NULL);
        CREATE TABLE dbo.GaoStoreProductImageRunsV1(StoreId int NOT NULL PRIMARY KEY,SourceDatabase sysname NOT NULL,
        PreviewManifestSha256 char(64) NOT NULL,SqlSha256 char(64) NOT NULL,
        ProductVariantBeforeHash varchar(100) NOT NULL,ProductVariantAfterHash varchar(100) NOT NULL,
        ProductVariantOtherColumnsHash varchar(100) NOT NULL,MediaAssetsHash varchar(100) NOT NULL,ProductImagesHash varchar(100) NOT NULL,
        CommittedAtUtc datetime2 NOT NULL);
        INSERT dbo.GaoStoreMigrationRunsV2 VALUES(N'keep-package',N'legacy',REPLICATE('A',64),N'keep-source',N'keep-target',SYSUTCDATETIME());
        INSERT dbo.GaoStoreProductImageRunsV1 VALUES(1,N'legacy',REPLICATE('A',64),REPLICATE('B',64),'before','after','other','media','images',SYSUTCDATETIME());
        """;
    private const string DropIndexes = """
        DROP INDEX IX_AutoInvoiceOperations_InvoiceHeadId ON dbo.AutoInvoiceOperations;
        DROP INDEX IX_AutoInvoiceOperationSources_AutoInvoiceOperationId ON dbo.AutoInvoiceOperationSources;
        DROP INDEX IX_AutoInvoiceOperationSources_InvoiceHeadId ON dbo.AutoInvoiceOperationSources;
        """;
    private static SqlServerDatabaseBaselinePreflight Preflight(AppDbContext db) => new(db,
        new EfCoreDatabaseMigrationCatalog(db), new SqlServerDatabaseObjectInventoryReader(db),
        new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));

    [Fact]
    public async Task Reported_server_state_repairs_only_missing_indexes_preserves_journals_and_upgrades_normally()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Prefix);
        await db.Database.ExecuteSqlRawAsync(Journals);
        Assert.True((await Preflight(db).InspectAsync()).IsAllowed);
        await db.Database.ExecuteSqlRawAsync(DropIndexes);
        var before = await new DatabaseSchemaInspection(db).ReadAsync();
        Assert.Equal(3, before.ExpectedOnly.Count);
        Assert.Empty(before.ActualOnly);
        Assert.Equal("StructuralSchemaMismatch", (await Preflight(db).InspectAsync()).SafeReasonCode);
        var repair = new AutoInvoiceIndexRepair(db);
        Assert.Equal("REPAIR_PLAN_VERIFIED", (await repair.RunAsync(false)).Status);
        Assert.Equal(3, (await new DatabaseSchemaInspection(db).ReadAsync()).ExpectedOnly.Count);
        var applied = await repair.RunAsync(true);
        Assert.Equal("AUTO_INVOICE_INDEX_REPAIR_VERIFIED", applied.Status);
        Assert.Equal(3, applied.Indexes.Count);
        Assert.Equal(before.AppliedMigrations, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal("ALREADY_COMPATIBLE", (await repair.RunAsync(true)).Status);
        Assert.Equal("keep-target", await db.Database.SqlQueryRaw<string>("SELECT TargetHashes AS [Value] FROM dbo.GaoStoreMigrationRunsV2 WHERE PackageId=N'keep-package'").SingleAsync());
        Assert.Equal("images", await db.Database.SqlQueryRaw<string>("SELECT ProductImagesHash AS [Value] FROM dbo.GaoStoreProductImageRunsV1 WHERE StoreId=1").SingleAsync());
        Assert.Equal(DatabaseCompatibilityState.SupportedPendingUpgrade, (await Preflight(db).InspectAsync()).State);
        await db.Database.MigrateAsync();
        Assert.Equal(DatabaseCompatibilityState.CurrentBaseline, (await Preflight(db).InspectAsync()).State);
        Assert.Equal(60, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task Unknown_tables_or_modified_journal_structure_are_not_ignored_and_repair_refuses()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(Prefix);
        await db.Database.ExecuteSqlRawAsync(Journals);
        await db.Database.ExecuteSqlRawAsync(DropIndexes);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.GaoStoreMigrationRunsV2 ADD Unreviewed int NULL;");
        Assert.False((await Preflight(db).InspectAsync()).IsAllowed);
        await Assert.ThrowsAsync<MigratorConfigurationException>(() => new AutoInvoiceIndexRepair(db).RunAsync(true));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.GaoStoreMigrationRunsV2 DROP COLUMN Unreviewed; CREATE TABLE dbo.Unreviewed (Id int NOT NULL);");
        Assert.False((await Preflight(db).InspectAsync()).IsAllowed);
        await Assert.ThrowsAsync<MigratorConfigurationException>(() => new AutoInvoiceIndexRepair(db).RunAsync(true));
        Assert.Equal(3, (await new DatabaseSchemaInspection(db).ReadAsync()).ExpectedOnly.Count);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE dbo.Unreviewed; ALTER TABLE dbo.GaoStoreProductImageRunsV1 ALTER COLUMN ProductImagesHash varchar(100) NULL;");
        Assert.False((await Preflight(db).InspectAsync()).IsAllowed);
        await Assert.ThrowsAsync<MigratorConfigurationException>(() => new AutoInvoiceIndexRepair(db).RunAsync(true));
    }

    [Fact]
    public async Task Failure_during_second_index_rolls_back_first_index_and_keeps_history()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using (var setup = database.CreateContext())
        {
            await setup.GetService<IMigrator>().MigrateAsync(Prefix);
            await setup.Database.ExecuteSqlRawAsync(DropIndexes);
        }
        await using (var failing = database.CreateContext(new FailSecondIndex()))
            await Assert.ThrowsAsync<InvalidOperationException>(() => new AutoInvoiceIndexRepair(failing).RunAsync(true));
        await using var verify = database.CreateContext();
        var report = await new DatabaseSchemaInspection(verify).ReadAsync();
        Assert.Equal(3, report.ExpectedOnly.Count);
        Assert.Empty(report.ActualOnly);
        Assert.Equal(43, report.AppliedMigrations.Count);
    }

    private sealed class FailSecondIndex : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("CREATE INDEX [IX_AutoInvoiceOperationSources_AutoInvoiceOperationId]"))
                throw new InvalidOperationException("Simulated index creation failure");
            return ValueTask.FromResult(result);
        }
    }
}
