using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

        var action = () => pipeline.RunAsync(MigratorMode.Bootstrap);

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

        var action = () => pipeline.RunAsync(MigratorMode.Bootstrap);

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

        await pipeline.RunAsync(MigratorMode.Bootstrap);

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

        await pipeline.RunAsync(MigratorMode.Bootstrap);
        await database.ExecuteAsync("UPDATE dbo.AdminMenuItems SET Title=N'Human menu', SortOrder=731, IsActive=0 WHERE Id=(SELECT MIN(Id) FROM dbo.AdminMenuItems)");
        var afterFirst =
            await database.ReadProvisioningStateSignatureAsync();
        db.ChangeTracker.Clear();
        await pipeline.RunAsync(MigratorMode.Bootstrap);

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

        var action = () => pipeline.RunAsync(MigratorMode.Bootstrap, cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Demo_seed_failure_should_leave_existing_data_unchanged()
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

        var action = () => pipeline.RunAsync(MigratorMode.DemoSeed);

        await action.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("Synthetic demo seed failure.");
        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(before);
    }

    [Fact]
    public async Task Explicit_security_seed_should_be_idempotent()
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

        await pipeline.RunAsync(MigratorMode.SecuritySeed);
        var afterFirst =
            await database.ReadProvisioningStateSignatureAsync();
        db.ChangeTracker.Clear();
        await pipeline.RunAsync(MigratorMode.SecuritySeed);

        (await database.ReadProvisioningStateSignatureAsync())
            .Should().Be(afterFirst);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Schema_only_preserves_customized_security_and_business_rows(bool pendingUpgrade)
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        if (pendingUpgrade)
            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("20260912120000_AddCustomerDisplayWifi");
        else
            await db.Database.MigrateAsync();

        // Fixture owns all provisioning; this does not model normal deployment.
        var bootstrap = new ProductionBootstrapper(db, Options.Create(CreateBootstrapOptions()));
        await new EfCoreProvisioningTransactionRunner(db).ExecuteAsync(async ct =>
        {
            await bootstrap.ApplyAsync(await bootstrap.InspectAsync(ct), ct);
        });
        await database.ExecuteAsync("""
            UPDATE dbo.AdminMenuItems SET Title = N'Human customized menu', SortOrder = 731, IsActive = 0
            WHERE Id = (SELECT MIN(Id) FROM dbo.AdminMenuItems);
            UPDATE dbo.Roles SET Name = N'Human customized role';
            DELETE FROM dbo.RolePermissions WHERE PermissionId = (SELECT MIN(Id) FROM dbo.Permissions);
            UPDATE dbo.Permissions SET Name = N'Human permission name';
            UPDATE dbo.Stores SET Name = N'Human store name';
            UPDATE dbo.LegalEntities SET Name = N'Human legal entity';
            UPDATE dbo.Warehouses SET Name = N'Human warehouse';
            """);
        var tables = new[] { "AdminMenuItems", "Permissions", "Roles", "RolePermissions", "Users", "UserInStores", "Stores", "LegalEntities", "Warehouses", "POSTerminals" };
        var before = new Dictionary<string, string>();
        foreach (var table in tables)
            before[table] = await database.ReadScalarAsync<string>($"SELECT (SELECT * FROM dbo.[{table}] FOR JSON PATH, INCLUDE_NULL_VALUES)");
        var pipeline = new MigrationExecutionPipeline(Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()), new TestHostEnvironment(Environments.Production),
            new MigratorConfigurationValidator(), CreatePreflight(db), new EfCoreDatabaseMigrationExecutor(db),
            new MandatorySecuritySeeder(db), new ThrowingDemoSeeder(), new DisabledBootstrapper(), new EfCoreProvisioningTransactionRunner(db));
        await pipeline.RunAsync(MigratorMode.SchemaOnly);
        await pipeline.RunAsync(MigratorMode.SchemaOnly);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        foreach (var table in tables)
            Assert.Equal(before[table], await database.ReadScalarAsync<string>($"SELECT (SELECT * FROM dbo.[{table}] FOR JSON PATH, INCLUDE_NULL_VALUES)"));
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
