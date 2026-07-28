using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
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
public sealed class AtomicProvisioningTests
{
    [Fact]
    public async Task Current_baseline_partial_bootstrap_state_should_fail_before_mandatory_seed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        db.Stores.Add(new Store
        {
            Name = "Existing Partial Store",
            SubDomain = "existing-partial",
            SubDomainNormalized = "existing-partial",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before =
            await database.ReadProvisioningStateSignatureAsync();
        var pipeline = CreateProductionPipeline(
            db,
            CreateBootstrapOptions(),
            new MandatorySecuritySeeder(db),
            new ProductionBootstrapper(
                db,
                Options.Create(CreateBootstrapOptions())));

        var action = () => pipeline.RunAsync();

        await action.Should()
            .ThrowAsync<ProductionBootstrapStateException>()
            .WithMessage("*ReasonCode=PartialOrDifferent*");
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Production_bootstrap_apply_failure_should_rollback_mandatory_seed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var before =
            await database.ReadProvisioningStateSignatureAsync();
        var pipeline = CreateProductionPipeline(
            db,
            CreateBootstrapOptions(),
            new MandatorySecuritySeeder(db),
            new ThrowingBootstrapper());

        var action = () => pipeline.RunAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Synthetic bootstrap apply failure.");
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Production_bootstrap_empty_state_should_commit_all_once()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var options = CreateBootstrapOptions();
        var pipeline = CreateProductionPipeline(
            db,
            options,
            new MandatorySecuritySeeder(db),
            new ProductionBootstrapper(
                db,
                Options.Create(options)));

        await pipeline.RunAsync();

        (await db.Stores.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.UserInStores.IgnoreQueryFilters().CountAsync())
            .Should().Be(1);
        (await db.Warehouses.IgnoreQueryFilters().CountAsync())
            .Should().Be(1);
        (await db.POSTerminals.IgnoreQueryFilters().CountAsync())
            .Should().Be(1);
        (await db.LegalEntities.IgnoreQueryFilters().CountAsync())
            .Should().Be(1);
    }

    [Fact]
    public async Task Production_bootstrap_matching_state_rerun_should_be_idempotent()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var options = CreateBootstrapOptions();
        var pipeline = CreateProductionPipeline(
            db,
            options,
            new MandatorySecuritySeeder(db),
            new ProductionBootstrapper(
                db,
                Options.Create(options)));

        await pipeline.RunAsync();
        var afterFirst =
            await database.ReadProvisioningStateSignatureAsync();
        db.ChangeTracker.Clear();
        await pipeline.RunAsync();

        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(afterFirst);
    }

    [Fact]
    public async Task Caller_cancellation_during_atomic_provisioning_should_rollback()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var before =
            await database.ReadProvisioningStateSignatureAsync();
        using var cts = new CancellationTokenSource();
        var pipeline = CreateProductionPipeline(
            db,
            CreateBootstrapOptions(),
            new MandatorySecuritySeeder(db),
            new CancelingBootstrapper(cts));

        var action = () => pipeline.RunAsync(cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Demo_seed_failure_should_rollback_mandatory_seed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var before =
            await database.ReadProvisioningStateSignatureAsync();
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            }),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(Environments.Development),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            new CountingMigrationExecutor(),
            new MandatorySecuritySeeder(db),
            new ThrowingDemoSeeder(),
            new DisabledBootstrapper(),
            new EfCoreProvisioningTransactionRunner(db));

        var action = () => pipeline.RunAsync();

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Synthetic demo seed failure.");
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Neither_mode_mandatory_seed_should_be_idempotent()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(Environments.Production),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            new CountingMigrationExecutor(),
            new MandatorySecuritySeeder(db),
            new ThrowingDemoSeeder(),
            new DisabledBootstrapper(),
            new EfCoreProvisioningTransactionRunner(db));

        await pipeline.RunAsync();
        var afterFirst =
            await database.ReadProvisioningStateSignatureAsync();
        db.ChangeTracker.Clear();
        await pipeline.RunAsync();

        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(afterFirst);
    }

    private static MigrationExecutionPipeline CreateProductionPipeline(
        AppDbContext db,
        ProductionBootstrapOptions options,
        IMandatorySecuritySeeder mandatorySeeder,
        IProductionBootstrapper bootstrapper)
        => new(
            Options.Create(new SeedDataOptions()),
            Options.Create(options),
            new TestHostEnvironment(Environments.Production),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            new CountingMigrationExecutor(),
            mandatorySeeder,
            new ThrowingDemoSeeder(),
            bootstrapper,
            new EfCoreProvisioningTransactionRunner(db));

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        AppDbContext db)
        => new(
            db,
            new EfCoreDatabaseMigrationCatalog(db),
            new SqlServerDatabaseObjectInventoryReader(db),
            new EfCoreDatabaseSchemaManifestCatalog(db),
            new SqlServerSchemaSnapshotReader(db));

    private static ProductionBootstrapOptions CreateBootstrapOptions()
        => new()
        {
            Enabled = true,
            StoreName = "Atomic Acceptance Store",
            StoreSubdomain = "atomic-acceptance",
            LegalEntityCode = "LEGAL-ATOMIC",
            LegalEntityName = "Atomic Legal Entity",
            LegalEntityLegalName = "Atomic Legal Entity Limited",
            WarehouseCode = "WAREHOUSE-ATOMIC",
            WarehouseName = "Atomic Warehouse",
            TerminalCode = "POS-ATOMIC",
            TerminalName = "Atomic POS",
            AdminUserName = "atomic.admin",
            AdminFullName = "Atomic Administrator",
            AdminEmail = "atomic.admin@example.invalid",
            AdminPassword = "Synthetic-Atomic-Password-42!"
        };

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

    private sealed class ThrowingDemoSeeder : IDemoDataSeeder
    {
        public Task SeedAsync(
            SeedDataOptions options,
            CancellationToken ct = default)
            => throw new InvalidOperationException(
                "Synthetic demo seed failure.");
    }

    private sealed class ThrowingBootstrapper : IProductionBootstrapper
    {
        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
            => Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true));

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
            => throw new InvalidOperationException(
                "Synthetic bootstrap apply failure.");
    }

    private sealed class CancelingBootstrapper : IProductionBootstrapper
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelingBootstrapper(
            CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
            => Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true));

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
        {
            _cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException(
                "Cancellation did not propagate.");
        }
    }

    private sealed class DisabledBootstrapper : IProductionBootstrapper
    {
        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
            => Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Disabled,
                RequiresChanges: false));

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
            => Task.FromResult(ProductionBootstrapResult.Disabled);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "GaoApp.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
