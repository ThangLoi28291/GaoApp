using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseObjectInventoryTests
{
    [Fact]
    public Task Existing_database_with_canary_view_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE VIEW [dbo].[AcceptanceCanaryView]
            AS SELECT CAST(1 AS int) AS [Value];
            """);

    [Fact]
    public Task Existing_database_with_stored_procedure_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE PROCEDURE [dbo].[AcceptanceCanaryProcedure]
            AS
            BEGIN
                SELECT CAST(1 AS int) AS [Value];
            END;
            """);

    [Fact]
    public Task Existing_database_with_scalar_function_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE FUNCTION [dbo].[AcceptanceCanaryScalar]()
            RETURNS int
            AS
            BEGIN
                RETURN 1;
            END;
            """);

    [Fact]
    public Task Existing_database_with_table_valued_function_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE FUNCTION [dbo].[AcceptanceCanaryTable]()
            RETURNS TABLE
            AS
            RETURN
            (
                SELECT CAST(1 AS int) AS [Value]
            );
            """);

    [Fact]
    public Task Existing_database_with_sequence_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE SEQUENCE [dbo].[AcceptanceCanarySequence]
            AS bigint
            START WITH 1
            INCREMENT BY 1;
            """);

    [Fact]
    public Task Existing_database_with_synonym_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE SYNONYM [dbo].[AcceptanceCanarySynonym]
            FOR [sys].[objects];
            """);

    [Fact]
    public Task Existing_database_with_custom_schema_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE SCHEMA [AcceptanceCanarySchema];
            """);

    [Fact]
    public Task Existing_database_with_user_defined_type_should_be_rejected_before_ddl()
        => AssertForeignObjectRejectedAsync("""
            CREATE TYPE [dbo].[AcceptanceCanaryType]
            FROM nvarchar(32) NOT NULL;
            """);

    [Fact]
    public async Task Truly_empty_existing_database_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.IsAllowed.Should().BeTrue();
        result.State.Should().Be(DatabaseCompatibilityState.ExistingEmpty);
        result.StructuralObjectCount.Should().Be(0);
        result.SecurityMetadataCounts.Should().Be(
            DatabaseSecurityMetadataCounts.Empty);
    }

    [Fact]
    public async Task Caller_cancellation_before_object_inventory_should_propagate_without_ddl_or_dml()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        var signatureBefore =
            await database.ReadDatabaseObjectSignatureAsync();
        await using var db = database.CreateContext();
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
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => pipeline.RunAsync(MigratorMode.SchemaOnly, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.Count.Should().Be(0);
        transaction.Count.Should().Be(0);
        (await database.ReadDatabaseObjectSignatureAsync())
            .Should().Be(signatureBefore);
        (await database.ObjectExistsAsync(
                "dbo",
                "__EFMigrationsHistory"))
            .Should().BeFalse();
        (await database.ObjectExistsAsync("dbo", "Stores"))
            .Should().BeFalse();
    }

    private static async Task AssertForeignObjectRejectedAsync(
        string createSql)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync(createSql);
        var signatureBefore =
            await database.ReadDatabaseObjectSignatureAsync();
        await using var db = database.CreateContext();
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

        var action = () => pipeline.RunAsync(MigratorMode.SchemaOnly);

        var exception = await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        exception.Which.Result.IsAllowed.Should().BeFalse();
        exception.Which.Result.State.Should().Be(
            DatabaseCompatibilityState.UnexpectedUserObjects);
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.Count.Should().Be(0);
        transaction.Count.Should().Be(0);
        (await database.ReadDatabaseObjectSignatureAsync())
            .Should().Be(signatureBefore);
        (await database.ObjectExistsAsync(
                "dbo",
                "__EFMigrationsHistory"))
            .Should().BeFalse();
        (await database.ObjectExistsAsync("dbo", "Stores"))
            .Should().BeFalse();
    }

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        GaoApp.Infrastructure.Data.AppDbContext db)
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
