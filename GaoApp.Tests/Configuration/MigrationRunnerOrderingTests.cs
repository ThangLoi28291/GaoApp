using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class MigrationRunnerOrderingTests
{
    [Fact]
    public async Task Demo_seed_and_production_bootstrap_enabled_should_fail_before_database()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(enabled: true),
            Environments.Development);

        var action = () => harness.Runner.RunAsync();

        var exception = await action.Should()
            .ThrowAsync<MigratorConfigurationException>();
        exception.Which.Message.Should()
            .Contain("SeedData:EnableDemoSeed")
            .And.Contain("ProductionBootstrap:Enabled");
        harness.DatabaseAccessCount.Should().Be(0);
        harness.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Production_demo_seed_should_fail_even_when_bootstrap_disabled()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(enabled: false),
            Environments.Production);

        var action = () => harness.Runner.RunAsync();

        await action.Should()
            .ThrowAsync<MigratorConfigurationException>()
            .WithMessage("*SeedData:EnableDemoSeed*Production*");
        harness.DatabaseAccessCount.Should().Be(0);
        harness.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Development_demo_seed_without_bootstrap_should_remain_allowed()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(enabled: false),
            Environments.Development);

        await harness.Runner.RunAsync();

        harness.Events.Should().Equal(
            "Preflight",
            "Migrate",
            "ProvisioningTransactionBegin",
            "MandatorySeed",
            "DemoSeed",
            "ProvisioningTransactionCommit");
        harness.DemoSeedCount.Should().Be(1);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    [Fact]
    public async Task Production_bootstrap_without_demo_seed_should_remain_allowed()
    {
        var harness = CreateHarness(
            new SeedDataOptions(),
            CreateValidBootstrapOptions(enabled: true),
            Environments.Production);

        await harness.Runner.RunAsync();

        harness.Events.Should().Equal(
            "Preflight",
            "Migrate",
            "BootstrapInspect",
            "ProvisioningTransactionBegin",
            "BootstrapInspect",
            "MandatorySeed",
            "ProductionBootstrap",
            "ProvisioningTransactionCommit");
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(1);
    }

    [Fact]
    public async Task Neither_optional_mode_should_run_after_mandatory_seed()
    {
        var harness = CreateHarness(
            new SeedDataOptions(),
            CreateValidBootstrapOptions(enabled: false),
            Environments.Production);

        await harness.Runner.RunAsync();

        harness.Events.Should().Equal(
            "Preflight",
            "Migrate",
            "ProvisioningTransactionBegin",
            "MandatorySeed",
            "ProvisioningTransactionCommit");
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_preflight_should_not_migrate_or_run_any_seed()
    {
        var harness = CreateHarness(
            new SeedDataOptions(),
            CreateValidBootstrapOptions(enabled: true),
            Environments.Production,
            preflightAllowed: false);

        var action = () => harness.Runner.RunAsync();

        await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        harness.Events.Should().Equal("Preflight");
        harness.MigrateCount.Should().Be(0);
        harness.MandatorySeedCount.Should().Be(0);
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_combined_modes_with_unreachable_sql_should_not_access_database()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Never-Log-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(
                enabled: true,
                adminPassword: "Never-Log-Admin-Password-42!"),
            Environments.Production);

        var action = () => harness.Runner.RunAsync();

        var exception = await action.Should()
            .ThrowAsync<MigratorConfigurationException>();
        harness.DatabaseAccessCount.Should().Be(0);
        exception.Which.ToString().Should()
            .NotContain("Never-Log-Demo-Password-42!")
            .And.NotContain("Never-Log-Admin-Password-42!");
    }

    [Fact]
    public async Task Validation_error_should_not_contain_demo_or_admin_password()
    {
        const string demoPassword = "Do-Not-Expose-Demo-42!";
        const string adminPassword = "Do-Not-Expose-Admin-42!";
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = demoPassword
            },
            CreateValidBootstrapOptions(
                enabled: true,
                adminPassword: adminPassword),
            Environments.Production);

        var action = () => harness.Runner.RunAsync();

        var exception = await action.Should()
            .ThrowAsync<MigratorConfigurationException>();
        exception.Which.ToString().Should()
            .NotContain(demoPassword)
            .And.NotContain(adminPassword);
    }

    [Fact]
    public async Task Invalid_mode_should_not_apply_migration_or_create_user()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(enabled: true),
            Environments.Development);

        var action = () => harness.Runner.RunAsync();

        await action.Should()
            .ThrowAsync<MigratorConfigurationException>();
        harness.MigrateCount.Should().Be(0);
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_preflight_should_not_run_demo_seed()
    {
        var harness = CreateHarness(
            new SeedDataOptions
            {
                EnableDemoSeed = true,
                DemoUserPassword = "Synthetic-Demo-Password-42!"
            },
            CreateValidBootstrapOptions(enabled: false),
            Environments.Development,
            preflightAllowed: false);

        var action = () => harness.Runner.RunAsync();

        await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_preflight_should_not_run_production_bootstrap()
    {
        var harness = CreateHarness(
            new SeedDataOptions(),
            CreateValidBootstrapOptions(enabled: true),
            Environments.Production,
            preflightAllowed: false);

        var action = () => harness.Runner.RunAsync();

        await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        harness.DemoSeedCount.Should().Be(0);
        harness.ProductionBootstrapCount.Should().Be(0);
    }

    private static RunnerHarness CreateHarness(
        SeedDataOptions seedOptions,
        ProductionBootstrapOptions bootstrapOptions,
        string environmentName,
        bool preflightAllowed = true)
        => new(
            seedOptions,
            bootstrapOptions,
            environmentName,
            preflightAllowed);

    private static ProductionBootstrapOptions CreateValidBootstrapOptions(
        bool enabled,
        string adminPassword = "Synthetic-Bootstrap-Password-42!")
        => new()
        {
            Enabled = enabled,
            StoreName = "First Store",
            StoreSubdomain = "first-store",
            LegalEntityCode = "LEGAL-01",
            LegalEntityName = "Primary Legal Entity",
            LegalEntityLegalName = "Primary Legal Entity Limited",
            WarehouseCode = "WAREHOUSE-01",
            WarehouseName = "Primary Warehouse",
            TerminalCode = "POS-01",
            TerminalName = "Primary POS",
            AdminUserName = "initial.admin",
            AdminFullName = "Initial Administrator",
            AdminEmail = "initial.admin@example.invalid",
            AdminPassword = adminPassword
        };

    private sealed class RunnerHarness
    {
        private readonly TrackingPreflight _preflight;
        private readonly TrackingMigrationExecutor _migration;
        private readonly TrackingMandatorySeeder _mandatorySeeder;
        private readonly TrackingDemoSeeder _demoSeeder;
        private readonly TrackingProductionBootstrapper _bootstrapper;
        private readonly TrackingTransactionRunner _transactionRunner;

        public RunnerHarness(
            SeedDataOptions seedOptions,
            ProductionBootstrapOptions bootstrapOptions,
            string environmentName,
            bool preflightAllowed)
        {
            Events = [];
            _preflight = new TrackingPreflight(Events, preflightAllowed);
            _migration = new TrackingMigrationExecutor(Events);
            _mandatorySeeder = new TrackingMandatorySeeder(Events);
            _demoSeeder = new TrackingDemoSeeder(Events);
            _bootstrapper = new TrackingProductionBootstrapper(Events);
            _transactionRunner = new TrackingTransactionRunner(Events);

            Runner = new MigrationExecutionPipeline(
                Options.Create(seedOptions),
                Options.Create(bootstrapOptions),
                new TestHostEnvironment(environmentName),
                new MigratorConfigurationValidator(),
                _preflight,
                _migration,
                _mandatorySeeder,
                _demoSeeder,
                _bootstrapper,
                _transactionRunner);
        }

        public MigrationExecutionPipeline Runner { get; }
        public List<string> Events { get; }
        public int DatabaseAccessCount => _preflight.Count;
        public int MigrateCount => _migration.Count;
        public int MandatorySeedCount => _mandatorySeeder.Count;
        public int DemoSeedCount => _demoSeeder.Count;
        public int ProductionBootstrapCount => _bootstrapper.Count;
    }

    private sealed class TrackingPreflight
        : IDatabaseBaselinePreflight
    {
        private readonly List<string> _events;
        private readonly bool _allowed;

        public TrackingPreflight(List<string> events, bool allowed)
        {
            _events = events;
            _allowed = allowed;
        }

        public int Count { get; private set; }

        public Task<DatabaseCompatibilityResult> InspectAsync(
            CancellationToken ct = default)
        {
            Count++;
            _events.Add("Preflight");
            return Task.FromResult(new DatabaseCompatibilityResult(
                _allowed
                    ? DatabaseCompatibilityState.ExistingEmpty
                    : DatabaseCompatibilityState.UnexpectedUserTables,
                _allowed,
                _allowed
                    ? "ExistingEmptyDatabase"
                    : "UnexpectedUserTables",
                1,
                0,
                _allowed ? 0 : 1));
        }
    }

    private sealed class TrackingMigrationExecutor
        : IDatabaseMigrationExecutor
    {
        private readonly List<string> _events;

        public TrackingMigrationExecutor(List<string> events)
        {
            _events = events;
        }

        public int Count { get; private set; }

        public Task MigrateAsync(CancellationToken ct = default)
        {
            Count++;
            _events.Add("Migrate");
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingMandatorySeeder
        : IMandatorySecuritySeeder
    {
        private readonly List<string> _events;

        public TrackingMandatorySeeder(List<string> events)
        {
            _events = events;
        }

        public int Count { get; private set; }

        public Task SeedAsync(CancellationToken ct = default)
        {
            Count++;
            _events.Add("MandatorySeed");
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingDemoSeeder : IDemoDataSeeder
    {
        private readonly List<string> _events;

        public TrackingDemoSeeder(List<string> events)
        {
            _events = events;
        }

        public int Count { get; private set; }

        public Task SeedAsync(
            SeedDataOptions options,
            CancellationToken ct = default)
        {
            Count++;
            _events.Add("DemoSeed");
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingProductionBootstrapper
        : IProductionBootstrapper
    {
        private readonly List<string> _events;

        public TrackingProductionBootstrapper(List<string> events)
        {
            _events = events;
        }

        public int Count { get; private set; }

        public Task<ProductionBootstrapPlan> InspectAsync(
            CancellationToken ct = default)
        {
            _events.Add("BootstrapInspect");
            return Task.FromResult(new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true));
        }

        public Task<ProductionBootstrapResult> ApplyAsync(
            ProductionBootstrapPlan plan,
            CancellationToken ct = default)
        {
            Count++;
            _events.Add("ProductionBootstrap");
            return Task.FromResult(ProductionBootstrapResult.Created);
        }
    }

    private sealed class TrackingTransactionRunner
        : IProvisioningTransactionRunner
    {
        private readonly List<string> _events;

        public TrackingTransactionRunner(List<string> events)
        {
            _events = events;
        }

        public async Task ExecuteAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default)
        {
            _events.Add("ProvisioningTransactionBegin");
            await operation(ct);
            _events.Add("ProvisioningTransactionCommit");
        }
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
