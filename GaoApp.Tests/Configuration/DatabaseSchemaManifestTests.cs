using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseSchemaManifestTests
{
    [Fact]
    public Task Current_history_missing_required_column_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] DROP COLUMN [Name];");

    [Fact]
    public Task Current_history_unexpected_extra_column_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ADD [AcceptanceExtra] int NULL;");

    [Fact]
    public Task Current_history_missing_inventory_idempotency_column_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            ALTER TABLE [dbo].[InventoryTransactions]
                DROP COLUMN [IdempotencyKey];
            """);

    [Fact]
    public Task Current_history_wrong_column_type_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ALTER COLUMN [Name] nvarchar(201) NOT NULL;");

    [Fact]
    public Task Current_history_wrong_nullability_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Stores] ALTER COLUMN [Name] nvarchar(200) NULL;");

    [Fact]
    public Task Current_history_missing_primary_key_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[AuditLogs] DROP CONSTRAINT [PK_AuditLogs];");

    [Fact]
    public Task Current_history_missing_foreign_key_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            ALTER TABLE [dbo].[CustomerRewardLedgers]
            DROP CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId];
            """);

    [Fact]
    public Task Current_history_wrong_foreign_key_delete_action_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            ALTER TABLE [dbo].[CustomerRewardLedgers]
            DROP CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId];

            ALTER TABLE [dbo].[CustomerRewardLedgers]
            ADD CONSTRAINT
                [FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId]
            FOREIGN KEY ([VoucherId])
            REFERENCES [dbo].[CustomerRewardVouchers] ([Id])
            ON DELETE CASCADE;
            """);

    [Fact]
    public Task Current_history_missing_unique_index_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "DROP INDEX [IX_Stores_SubDomainNormalized] ON [dbo].[Stores];");

    [Fact]
    public Task Current_history_wrong_index_filter_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DROP INDEX
                [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId]
            ON [dbo].[InvoiceDetails];

            CREATE UNIQUE INDEX
                [IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLegalEntityAllocationId]
            ON [dbo].[InvoiceDetails]
                ([StoreId], [InvoiceHeadId], [OrderLegalEntityAllocationId])
            WHERE [OrderLegalEntityAllocationId] IS NOT NULL
              AND [IsDeleted] = 1;
            """);

    [Fact]
    public Task Current_history_wrong_inventory_idempotency_index_filter_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DROP INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions];

            CREATE UNIQUE INDEX
                [UX_InventoryTransactions_StoreId_IdempotencyKey_Active]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [IdempotencyKey])
            WHERE [IdempotencyKey] IS NOT NULL
              AND [IsDeleted] = 1;
            """);

    [Fact]
    public Task Current_history_missing_check_constraint_should_be_rejected()
        => AssertCorruptionRejectedAsync(
            "ALTER TABLE [dbo].[Taxes] DROP CONSTRAINT [CK_Taxes_Rate_0_100];");

    [Fact]
    public Task Current_history_wrong_default_constraint_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            DECLARE @constraintName sysname =
            (
                SELECT [default].[name]
                FROM [sys].[default_constraints] AS [default]
                INNER JOIN [sys].[columns] AS [column]
                    ON [column].[object_id] = [default].[parent_object_id]
                   AND [column].[column_id] =
                       [default].[parent_column_id]
                WHERE [default].[parent_object_id] =
                    OBJECT_ID(N'[dbo].[Stores]')
                  AND [column].[name] =
                    N'IsMultiLegalEntityEnabled'
            );

            DECLARE @dropSql nvarchar(max) =
                N'ALTER TABLE [dbo].[Stores] DROP CONSTRAINT '
                + QUOTENAME(@constraintName);

            EXEC [sys].[sp_executesql] @dropSql;

            ALTER TABLE [dbo].[Stores]
            ADD CONSTRAINT [DF_Acceptance_Stores_MultiLegal]
            DEFAULT (1) FOR [IsMultiLegalEntityEnabled];
            """);

    [Fact]
    public Task Current_history_extra_table_should_be_rejected()
        => AssertCorruptionRejectedAsync("""
            CREATE TABLE [dbo].[AcceptanceExtraTable]
            (
                [Id] int NOT NULL
                    CONSTRAINT [PK_AcceptanceExtraTable] PRIMARY KEY
            );
            """);

    [Fact]
    public async Task Exact_current_baseline_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();

        var result = await CreatePreflight(db).InspectAsync();

        result.IsAllowed.Should().BeTrue();
        result.State.Should().Be(DatabaseCompatibilityState.CurrentBaseline);
        result.SchemaMismatchCategoryCount.Should().Be(0);
    }

    [Fact]
    public async Task Current_baseline_rerun_should_be_no_op()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var migration = new EfCoreDatabaseMigrationExecutor(db);
        await migration.MigrateAsync();
        var historyBefore = await database.ReadMigrationHistoryAsync();
        var fingerprintBefore = await ReadFingerprintAsync(db);

        (await CreatePreflight(db).InspectAsync())
            .IsAllowed.Should().BeTrue();
        await migration.MigrateAsync();

        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyBefore);
        (await ReadFingerprintAsync(db)).Should().Be(fingerprintBefore);
    }

    [Fact]
    public async Task Known_prefix_with_manifest_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var sourceIds = db.Database.GetMigrations().ToList();
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);

        catalog.TryGetManifestForAppliedMigrationPrefix(
                sourceIds,
                out var manifest)
            .Should().BeTrue();
        manifest.AppliedMigrationIds.Should().Equal(sourceIds);
        (await CreatePreflight(db).InspectAsync())
            .IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_migration_prefix_without_manifest_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory]
                    PRIMARY KEY ([MigrationId])
            );
            """);
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.IsAllowed.Should().BeFalse();
        result.State.Should().Be(
            DatabaseCompatibilityState.UnsupportedMigrationPrefix);
        result.SafeReasonCode.Should().Be(
            "EmptyHistoryTableUnsupported");
    }

    private static async Task AssertCorruptionRejectedAsync(
        string corruptionSql)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var historyBefore = await database.ReadMigrationHistoryAsync();
        await database.ExecuteAsync(corruptionSql);
        var corruptFingerprint = await ReadFingerprintAsync(db);
        var migration = new CountingMigrationExecutor();
        var mandatory = new CountingMandatorySeeder();
        var demo = new CountingDemoSeeder();
        var bootstrap = new CountingBootstrapper();
        var transaction = new CountingTransactionRunner();
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            migration,
            mandatory,
            demo,
            bootstrap,
            transaction);

        var action = () => pipeline.RunAsync();

        var exception = await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        exception.Which.Result.State.Should().Be(
            DatabaseCompatibilityState.PartialOrCorruptBaseline);
        exception.Which.Result.SafeReasonCode.Should().Be(
            "StructuralSchemaMismatch");
        exception.Which.Result.SchemaMismatchCategoryCount
            .Should().BeGreaterThan(0);
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.Count.Should().Be(0);
        transaction.Count.Should().Be(0);
        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(historyBefore);
        (await ReadFingerprintAsync(db)).Should().Be(corruptFingerprint);
    }

    private static async Task<string> ReadFingerprintAsync(
        AppDbContext db)
    {
        var wasOpen =
            db.Database.GetDbConnection().State
            == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await db.Database.OpenConnectionAsync();
        }

        try
        {
            var migrationIds = (await db.Database
                    .GetAppliedMigrationsAsync())
                .ToList();
            var snapshot = await new SqlServerSchemaSnapshotReader(db)
                .ReadAsync(migrationIds);
            return snapshot.Fingerprint;
        }
        finally
        {
            if (!wasOpen)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        AppDbContext db)
        => new(
            db,
            new EfCoreDatabaseMigrationCatalog(db),
            new SqlServerDatabaseObjectInventoryReader(db),
            new EfCoreDatabaseSchemaManifestCatalog(db),
            new SqlServerSchemaSnapshotReader(db));

    private sealed class CountingMigrationExecutor
        : IDatabaseMigrationExecutor
    {
        public int Count { get; private set; }

        public Task MigrateAsync(CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingMandatorySeeder
        : IMandatorySecuritySeeder
    {
        public int Count { get; private set; }

        public Task SeedAsync(CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingDemoSeeder : IDemoDataSeeder
    {
        public int Count { get; private set; }

        public Task SeedAsync(
            SeedDataOptions options,
            CancellationToken ct = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingBootstrapper : IProductionBootstrapper
    {
        public int Count { get; private set; }

        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true));
        }

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
        {
            Count++;
            return Task.FromResult(ProductionBootstrapResult.Created);
        }
    }

    private sealed class CountingTransactionRunner
        : IProvisioningTransactionRunner
    {
        public int Count { get; private set; }

        public Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            Count++;
            return operation(ct);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
