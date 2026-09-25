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
    [Theory]
    [InlineData(MigratorMode.SchemaOnly)]
    [InlineData(MigratorMode.SecuritySeed)]
    [InlineData(MigratorMode.Bootstrap)]
    [InlineData(MigratorMode.DemoSeed)]
    public async Task Explicit_mode_runs_only_requested_operation(MigratorMode mode)
    {
        var harness = CreateHarness(new SeedDataOptions
        {
            EnableDemoSeed = mode == MigratorMode.DemoSeed,
            DemoUserPassword = "Synthetic-Demo-Password-42!"
        }, CreateValidBootstrapOptions(mode == MigratorMode.Bootstrap), Environments.Development);
        await harness.Runner.RunAsync(mode);
        harness.MigrateCount.Should().Be(mode == MigratorMode.SchemaOnly ? 1 : 0);
        harness.MandatorySeedCount.Should().Be(mode == MigratorMode.SecuritySeed ? 1 : 0);
        harness.DemoSeedCount.Should().Be(mode == MigratorMode.DemoSeed ? 1 : 0);
        harness.ProductionBootstrapCount.Should().Be(mode == MigratorMode.Bootstrap ? 1 : 0);
        if (mode == MigratorMode.SchemaOnly)
            harness.Events.Should().Equal("Preflight", "Migrate", "Preflight");
        if (mode == MigratorMode.Bootstrap)
            harness.Events.Should().Equal("Preflight", "BootstrapInspect", "ProvisioningTransactionBegin", "BootstrapInspect", "ProductionBootstrap", "ProvisioningTransactionCommit");
    }

    [Theory]
    [InlineData(MigratorMode.Unspecified)]
    [InlineData((MigratorMode)999)]
    public async Task Invalid_mode_has_no_database_access(MigratorMode mode)
    {
        var harness = CreateHarness(new(), new(), Environments.Production);
        await Assert.ThrowsAsync<MigratorConfigurationException>(() => harness.Runner.RunAsync(mode));
        harness.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MigratorMode.SchemaOnly)]
    [InlineData(MigratorMode.SecuritySeed)]
    [InlineData(MigratorMode.Bootstrap)]
    [InlineData(MigratorMode.DemoSeed)]
    public async Task Rejected_preflight_never_enters_any_mutation(MigratorMode mode)
    {
        var harness = CreateHarness(new SeedDataOptions { EnableDemoSeed = mode == MigratorMode.DemoSeed, DemoUserPassword = "Synthetic-42-Password!" },
            CreateValidBootstrapOptions(mode == MigratorMode.Bootstrap), Environments.Development, false);
        await Assert.ThrowsAsync<DatabaseCompatibilityException>(() => harness.Runner.RunAsync(mode));
        harness.Events.Should().Equal("Preflight");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task Schema_mode_rejects_ambient_seed_flags_before_database(bool demo, bool bootstrap, bool admin)
    {
        var harness = CreateHarness(new SeedDataOptions { EnableDemoSeed = demo, EnableDefaultAdminSeed = admin, DemoUserPassword = "Never-Log-42-Password!" },
            CreateValidBootstrapOptions(bootstrap), Environments.Development);
        var error = await Assert.ThrowsAsync<MigratorConfigurationException>(() => harness.Runner.RunAsync(MigratorMode.SchemaOnly));
        harness.Events.Should().BeEmpty();
        error.ToString().Should().NotContain("Never-Log-42-Password!");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task Demo_is_development_only(string environment)
    {
        var harness = CreateHarness(new SeedDataOptions { EnableDemoSeed = true, DemoUserPassword = "Synthetic-42-Password!" }, new(), environment);
        await Assert.ThrowsAsync<MigratorConfigurationException>(() => harness.Runner.RunAsync(MigratorMode.DemoSeed));
        harness.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("--schema-only --security-seed")]
    [InlineData("--schema-only --schema-only")]
    [InlineData("--Schema-Only")]
    [InlineData("--unknown")]
    [InlineData("--schema-only=true")]
    public void Missing_invalid_ambiguous_arguments_fail_closed(string command)
        => Assert.Throws<MigratorConfigurationException>(() => MigratorCommand.Parse(command.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Theory]
    [InlineData("--schema-only", MigratorMode.SchemaOnly)]
    [InlineData("--security-seed", MigratorMode.SecuritySeed)]
    [InlineData("--bootstrap", MigratorMode.Bootstrap)]
    [InlineData("--demo-seed", MigratorMode.DemoSeed)]
    public void Exact_single_mode_is_accepted(string command, MigratorMode mode)
        => Assert.Equal(mode, MigratorCommand.Parse([command]));

    [Fact]
    public async Task Schema_verification_failure_is_failure_even_after_migration_returns()
    {
        var harness = CreateHarness(new(), new(), Environments.Production);
        harness.DatabaseState = DatabaseCompatibilityState.ExistingEmpty;
        await Assert.ThrowsAsync<DatabaseCompatibilityException>(() => harness.Runner.RunAsync(MigratorMode.SchemaOnly));
        harness.Events.Should().Equal("Preflight", "Migrate", "Preflight");
        harness.MandatorySeedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(MigratorMode.SecuritySeed)]
    [InlineData(MigratorMode.Bootstrap)]
    [InlineData(MigratorMode.DemoSeed)]
    public async Task Data_operations_refuse_empty_or_pending_schema_without_migrating(MigratorMode mode)
    {
        var harness = CreateHarness(new SeedDataOptions { EnableDemoSeed = mode == MigratorMode.DemoSeed, DemoUserPassword = "Synthetic-42-Password!" },
            CreateValidBootstrapOptions(mode == MigratorMode.Bootstrap), Environments.Development);
        harness.DatabaseState = DatabaseCompatibilityState.ExistingEmpty;
        await Assert.ThrowsAsync<DatabaseCompatibilityException>(() => harness.Runner.RunAsync(mode));
        harness.Events.Should().Equal("Preflight");
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

        public DatabaseCompatibilityState DatabaseState { get => _preflight.State; set => _preflight.State = value; }
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

        public DatabaseCompatibilityState State { get; set; } = DatabaseCompatibilityState.CurrentBaseline;
        public int Count { get; private set; }

        public Task<DatabaseCompatibilityResult> InspectAsync(
            CancellationToken ct = default)
        {
            Count++;
            _events.Add("Preflight");
            return Task.FromResult(new DatabaseCompatibilityResult(
                _allowed
                    ? State
                    : DatabaseCompatibilityState.UnexpectedUserTables,
                _allowed,
                _allowed
                    ? "ExistingEmptyDatabase"
                    : "UnexpectedUserTables",
                1,
                State == DatabaseCompatibilityState.CurrentBaseline ? 1 : 0,
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
