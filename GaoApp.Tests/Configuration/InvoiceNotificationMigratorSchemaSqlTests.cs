using System.Data;
using System.Diagnostics;
using System.Text.Json;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

// Only the fixture's guarded GUID database is created/migrated/corrupted/dropped.
// No Web host, business configuration connection or security/menu seeder is run.
[Collection("SqlServerConcurrency")]
public sealed class InvoiceNotificationMigratorSchemaSqlTests
{
    private const string BeforeLastThree = "20261014140000_AddBankNotificationBridge";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fresh_and_79_to_82_schema_only_runs_pass_full_inspection_and_rerun_without_seed_or_data_changes(bool upgrade)
    {
        await using var fixture = new InventoryPostingLocalDb();
        await using var db = fixture.CreateHostContext();
        string? original = null;
        if (upgrade)
        {
            await fixture.MigrateAsync(BeforeLastThree);
            await AddSentinelAsync(db);
            original = await ProtectedDataAsync(db);
            var prefix = await new DatabaseSchemaInspection(db).ReadAsync();
            Assert.Equal("MatchingAppliedPrefix", prefix.Status);
            Assert.Equal(79, prefix.AppliedMigrations.Count);
            Assert.Equal(BeforeLastThree, prefix.AppliedMigrations[^1]);
            Assert.Empty(prefix.ExpectedOnly); Assert.Empty(prefix.ActualOnly);
            var preflight = await Preflight(db).InspectAsync();
            Assert.True(preflight.IsAllowed);
            Assert.Equal(DatabaseCompatibilityState.SupportedPendingUpgrade, preflight.State);
        }
        var seeds = new ForbiddenDataOperations();
        var executor = new RecordingExecutor(db);
        var pipeline = Pipeline(db, executor, seeds);
        if (Environment.GetEnvironmentVariable("GAOAPP_MIGRATOR_TEST_DLL") is { Length: > 0 } published)
            await AssertPublishedEntryPointAsync(fixture, db, published, original);
        await pipeline.RunAsync(MigratorMode.SchemaOnly);
        await AssertCurrentAsync(db);
        await AssertOldGenericPathExplainsFourCheckRecordsAsync(db);
        if (original is not null) Assert.Equal(original, await ProtectedDataAsync(db));
        else await AddSentinelAsync(db);

        var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var fingerprint = await FingerprintAsync(db);
        var data = await ProtectedDataAsync(db);
        await pipeline.RunAsync(MigratorMode.SchemaOnly);
        await AssertCurrentAsync(db);
        Assert.Equal(history, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(fingerprint, await FingerprintAsync(db));
        Assert.Equal(data, await ProtectedDataAsync(db));
        Assert.Equal(2, executor.Count); Assert.Equal(0, seeds.Count);
        Assert.Equal(0, await db.AuditOutbox.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Set<InvoiceNotificationOutbox>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Weakened_disabled_and_untrusted_P3_source_CHECKs_are_rejected_without_migration_or_seed()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        await using var db = fixture.CreateHostContext();
        await AddSentinelAsync(db);
        await AssertCurrentAsync(db);
        var history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var data = await ProtectedDataAsync(db);
        var executor = new RecordingExecutor(db); var seeds = new ForbiddenDataOperations();
        var pipeline = Pipeline(db, executor, seeds);

        var original = InvoiceNotificationCheckSamples.Migration(InvoiceNotificationCheckSamples.SourceName);
        var broadened = original.Replace("'Order','Invoice'", "'Order','Invoice','InputInvoice'", StringComparison.Ordinal);
        Assert.NotEqual(original, broadened);
        await fixture.ExecuteAsync("ALTER TABLE dbo.InvoiceNotificationOutbox DROP CONSTRAINT CK_InvoiceNotificationOutbox_Source; " +
            "ALTER TABLE dbo.InvoiceNotificationOutbox WITH CHECK ADD CONSTRAINT CK_InvoiceNotificationOutbox_Source CHECK (" + broadened + ");");
        await AssertRejectedAsync();
        await fixture.ExecuteAsync("ALTER TABLE dbo.InvoiceNotificationOutbox DROP CONSTRAINT CK_InvoiceNotificationOutbox_Source; " +
            "ALTER TABLE dbo.InvoiceNotificationOutbox WITH CHECK ADD CONSTRAINT CK_InvoiceNotificationOutbox_Source CHECK (" + original + ");");
        await AssertCurrentAsync(db);

        await fixture.ExecuteAsync("ALTER TABLE dbo.InvoiceNotificationOutbox NOCHECK CONSTRAINT CK_InvoiceNotificationOutbox_Source;");
        await AssertRejectedAsync();
        await fixture.ExecuteAsync("ALTER TABLE dbo.InvoiceNotificationOutbox CHECK CONSTRAINT CK_InvoiceNotificationOutbox_Source;");
        Assert.Equal(1, await fixture.ExecuteScalarAsync<int>("SELECT CONVERT(int,is_not_trusted) FROM sys.check_constraints WHERE name='CK_InvoiceNotificationOutbox_Source';"));
        Assert.Equal(0, await fixture.ExecuteScalarAsync<int>("SELECT CONVERT(int,is_disabled) FROM sys.check_constraints WHERE name='CK_InvoiceNotificationOutbox_Source';"));
        await AssertRejectedAsync();
        await fixture.ExecuteAsync("ALTER TABLE dbo.InvoiceNotificationOutbox WITH CHECK CHECK CONSTRAINT CK_InvoiceNotificationOutbox_Source;");
        await AssertCurrentAsync(db);
        Assert.Equal(0, executor.Count); Assert.Equal(0, seeds.Count);

        async Task AssertRejectedAsync()
        {
            var fingerprint = await FingerprintAsync(db);
            var report = await new DatabaseSchemaInspection(db).ReadAsync();
            Assert.Equal("StructuralSchemaMismatch", report.Status);
            Assert.Equal(2, report.MismatchCounts!.CheckConstraints);
            Assert.Equal(2, report.MismatchCounts.Total);
            Assert.Single(report.ExpectedOnly); Assert.Single(report.ActualOnly);
            Assert.All(report.ExpectedOnly.Concat(report.ActualOnly), row => Assert.Contains("ck_invoicenotificationoutbox_source", row));
            var error = await Assert.ThrowsAsync<DatabaseCompatibilityException>(() => pipeline.RunAsync(MigratorMode.SchemaOnly));
            Assert.False(error.MigrationWasAttempted);
            Assert.Equal("StructuralSchemaMismatch", error.Result.SafeReasonCode);
            Assert.Equal(2, error.Result.SchemaMismatches!.CheckConstraints);
            Assert.Equal(history, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.Equal(fingerprint, await FingerprintAsync(db));
            Assert.Equal(data, await ProtectedDataAsync(db));
            Assert.Equal(0, executor.Count); Assert.Equal(0, seeds.Count);
        }
    }

    private static async Task AssertCurrentAsync(AppDbContext db)
    {
        var inspection = await new DatabaseSchemaInspection(db).ReadAsync();
        Assert.Equal("MatchingCurrentSchema", inspection.Status);
        Assert.Empty(inspection.PendingMigrations); Assert.Empty(inspection.ExpectedOnly); Assert.Empty(inspection.ActualOnly);
        Assert.Equal(0, inspection.MismatchCounts!.Total);
        Assert.Contains("20261027010000_AddInvoiceNotificationsP3", inspection.AppliedMigrations);
        var preflight = await Preflight(db).InspectAsync();
        Assert.True(preflight.IsAllowed, preflight.SafeReasonCode);
        Assert.Equal(DatabaseCompatibilityState.CurrentBaseline, preflight.State);
        Assert.Equal(preflight.SourceMigrationCount, preflight.AppliedMigrationCount);
        Assert.Equal(0, preflight.SchemaMismatchCategoryCount);
    }

    private static async Task AssertOldGenericPathExplainsFourCheckRecordsAsync(AppDbContext db)
    {
        var expected = new HashSet<string>(); var actual = new HashSet<string>();
        foreach (var name in InvoiceNotificationCheckSamples.Names)
        {
            var persisted = await db.Database.SqlQuery<string>($"SELECT [definition] AS [Value] FROM sys.check_constraints WHERE [name]={name}").SingleAsync();
            Assert.Equal(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(InvoiceNotificationCheckSamples.Migration(name)),
                DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(persisted));
            expected.Add(name + "|" + DatabaseSchemaNormalization.NormalizeSqlExpression(InvoiceNotificationCheckSamples.Migration(name)));
            actual.Add(name + "|" + DatabaseSchemaNormalization.NormalizeSqlExpression(persisted));
        }
        // The old generic path compared IN lists with SQL Server's expanded OR
        // text: two expected-only plus two actual-only records, matching checks=4.
        Assert.Equal(2, expected.Except(actual).Count()); Assert.Equal(2, actual.Except(expected).Count());
    }

    private static Task AddSentinelAsync(AppDbContext db) => db.Database.ExecuteSqlRawAsync("""
        INSERT dbo.Stores(Name,SubDomain,SubDomainNormalized,IsActive,CreatedAtUtc,IsDeleted)
        VALUES(N'Synthetic schema-only sentinel',N'p3-schema-sentinel',N'P3-SCHEMA-SENTINEL',1,SYSUTCDATETIME(),0);
        """);

    private static async Task AssertPublishedEntryPointAsync(InventoryPostingLocalDb fixture, AppDbContext db, string published, string? existingData)
    {
        Assert.True(Path.IsPathFullyQualified(published) && File.Exists(published), "Configured published Migrator DLL must be an existing absolute path.");
        var target = new SqlConnectionStringBuilder(fixture.ConnectionString);
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", target.InitialCatalog);
        var migrationCount = db.Database.GetMigrations().Count();
        string[]? history = null; string? fingerprint = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Path.GetDirectoryName(published)!, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(published); start.ArgumentList.Add("--schema-only");
            start.Environment["DOTNET_ENVIRONMENT"] = Environments.Production;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = Environments.Production;
            start.Environment["ConnectionStrings__DefaultConnection"] = fixture.ConnectionString;
            start.Environment["SeedData__EnableDemoSeed"] = "false";
            start.Environment["SeedData__EnableDefaultAdminSeed"] = "false";
            start.Environment["ProductionBootstrap__Enabled"] = "false";
            start.Environment["ProductionBootstrap__EnableBootstrap"] = "false";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            var diagnostic = await output + await error;
            Assert.True(process.ExitCode == 0, diagnostic);
            Assert.Contains($"SCHEMA_ONLY_VERIFIED; SourceMigrations={migrationCount}; AppliedMigrations={migrationCount}", diagnostic);
            await AssertCurrentAsync(db);
            if (existingData is not null) Assert.Equal(existingData, await ProtectedDataAsync(db));
            if (attempt == 0)
            {
                history = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
                fingerprint = await FingerprintAsync(db); existingData = await ProtectedDataAsync(db);
            }
            else
            {
                Assert.Equal(history, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
                Assert.Equal(fingerprint, await FingerprintAsync(db));
            }
        }
        Assert.Equal(0, await db.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Permissions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.AdminMenuItems.IgnoreQueryFilters().CountAsync());
    }

    private static async Task<string> ProtectedDataAsync(AppDbContext db) => JsonSerializer.Serialize(new
    {
        stores = await db.Stores.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.SubDomain, x.SubDomainNormalized, x.IsActive, x.IsDeleted }).ToArrayAsync(),
        menus = await db.AdminMenuItems.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.Title, x.Url, x.SortOrder, x.IsActive }).ToArrayAsync(),
        grants = await db.RolePermissions.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync(),
        users = await db.Users.IgnoreQueryFilters().CountAsync(),
        invoices = await db.InvoiceHeads.IgnoreQueryFilters().OrderBy(x => x.Id).Select(x => new { x.Id, x.GrandTotal }).ToArrayAsync()
    });

    private static async Task<string> FingerprintAsync(AppDbContext db)
    {
        var wasOpen = db.Database.GetDbConnection().State == ConnectionState.Open;
        try
        {
            if (!wasOpen) await db.Database.OpenConnectionAsync();
            return (await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray())).Fingerprint;
        }
        finally { if (!wasOpen) await db.Database.CloseConnectionAsync(); }
    }

    private static SqlServerDatabaseBaselinePreflight Preflight(AppDbContext db) => new(db,
        new EfCoreDatabaseMigrationCatalog(db), new SqlServerDatabaseObjectInventoryReader(db),
        new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));

    private static MigrationExecutionPipeline Pipeline(AppDbContext db, IDatabaseMigrationExecutor executor, ForbiddenDataOperations seeds)
        => new(Options.Create(new SeedDataOptions()), Options.Create(new ProductionBootstrapOptions()), new TestEnvironment(),
            new MigratorConfigurationValidator(), Preflight(db), executor, seeds, seeds, seeds, seeds);

    private sealed class RecordingExecutor(AppDbContext db) : IDatabaseMigrationExecutor
    {
        internal int Count { get; private set; }
        public Task MigrateAsync(CancellationToken ct = default) { Count++; return db.Database.MigrateAsync(ct); }
    }
    private sealed class ForbiddenDataOperations : IMandatorySecuritySeeder, IDemoDataSeeder, IProductionBootstrapper, IProvisioningTransactionRunner
    {
        internal int Count { get; private set; }
        private InvalidOperationException Rejected() { Count++; return new("Schema-only must not enter a data seeder or provisioning transaction."); }
        public Task SeedAsync(CancellationToken ct = default) => throw Rejected();
        public Task SeedAsync(SeedDataOptions options, CancellationToken ct = default) => throw Rejected();
        public Task<ProductionBootstrapPlan> InspectAsync(CancellationToken ct = default) => throw Rejected();
        public Task<ProductionBootstrapResult> ApplyAsync(ProductionBootstrapPlan plan, CancellationToken ct = default) => throw Rejected();
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => throw Rejected();
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
