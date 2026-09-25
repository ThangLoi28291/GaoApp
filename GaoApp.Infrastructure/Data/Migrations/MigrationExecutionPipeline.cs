using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class MigrationExecutionPipeline
{
    private readonly SeedDataOptions _seedOptions;
    private readonly ProductionBootstrapOptions _bootstrapOptions;
    private readonly IHostEnvironment _environment;
    private readonly MigratorConfigurationValidator _configurationValidator;
    private readonly IDatabaseBaselinePreflight _databasePreflight;
    private readonly IDatabaseMigrationExecutor _migrationExecutor;
    private readonly IMandatorySecuritySeeder _mandatorySecuritySeeder;
    private readonly IDemoDataSeeder _demoDataSeeder;
    private readonly IProductionBootstrapper _productionBootstrapper;
    private readonly IProvisioningTransactionRunner _transactionRunner;

    public MigrationExecutionPipeline(
        IOptions<SeedDataOptions> seedOptions,
        IOptions<ProductionBootstrapOptions> bootstrapOptions,
        IHostEnvironment environment,
        MigratorConfigurationValidator configurationValidator,
        IDatabaseBaselinePreflight databasePreflight,
        IDatabaseMigrationExecutor migrationExecutor,
        IMandatorySecuritySeeder mandatorySecuritySeeder,
        IDemoDataSeeder demoDataSeeder,
        IProductionBootstrapper productionBootstrapper,
        IProvisioningTransactionRunner transactionRunner)
    {
        _seedOptions = seedOptions.Value;
        _bootstrapOptions = bootstrapOptions.Value;
        _environment = environment;
        _configurationValidator = configurationValidator;
        _databasePreflight = databasePreflight;
        _migrationExecutor = migrationExecutor;
        _mandatorySecuritySeeder = mandatorySecuritySeeder;
        _demoDataSeeder = demoDataSeeder;
        _productionBootstrapper = productionBootstrapper;
        _transactionRunner = transactionRunner;
    }

    public async Task RunAsync(MigratorMode mode, CancellationToken ct = default)
    {
        _configurationValidator.ValidateMode(mode, _seedOptions, _bootstrapOptions,
            _environment.EnvironmentName);

        Console.WriteLine("=================================================");
        Console.WriteLine("GaoApp Migrator started");
        Console.WriteLine("=================================================");
        Console.WriteLine();
        Console.WriteLine("Configuration validation completed.");
        Console.WriteLine();

        var compatibility = await _databasePreflight.InspectAsync(ct);
        var schemaMismatches =
            compatibility.SchemaMismatches
            ?? new DatabaseSchemaMismatchCounts(
                Tables: 0,
                Columns: 0,
                PrimaryKeys: 0,
                ForeignKeys: 0,
                Indexes: 0,
                CheckConstraints: 0,
                Sequences: 0);
        var securityMetadata =
            compatibility.SecurityMetadataCounts
            ?? DatabaseSecurityMetadataCounts.Empty;

        Console.WriteLine(
            "Database preflight: {0}; SourceMigrations={1}; AppliedMigrations={2}; UserTables={3}; StructuralObjects={4}; SchemaMismatchCategories={5}; SchemaMismatchTables={6}; SchemaMismatchColumns={7}; SchemaMismatchPrimaryKeys={8}; SchemaMismatchForeignKeys={9}; SchemaMismatchIndexes={10}; SchemaMismatchChecks={11}; SchemaMismatchSequences={12}; ReasonCode={13}",
            compatibility.State,
            compatibility.SourceMigrationCount,
            compatibility.AppliedMigrationCount,
            compatibility.UserTableCount,
            compatibility.StructuralObjectCount,
            compatibility.SchemaMismatchCategoryCount,
            schemaMismatches.Tables,
            schemaMismatches.Columns,
            schemaMismatches.PrimaryKeys,
            schemaMismatches.ForeignKeys,
            schemaMismatches.Indexes,
            schemaMismatches.CheckConstraints,
            schemaMismatches.Sequences,
            compatibility.SafeReasonCode);
        Console.WriteLine(
            "Database security metadata audit: Users={0}; CustomRoles={1}; RoleMemberships={2}; Certificates={3}; AsymmetricKeys={4}; SymmetricKeys={5}; DatabaseScopedCredentials={6}.",
            securityMetadata.DatabaseUsers,
            securityMetadata.CustomDatabaseRoles,
            securityMetadata.RoleMemberships,
            securityMetadata.Certificates,
            securityMetadata.AsymmetricKeys,
            securityMetadata.SymmetricKeys,
            securityMetadata.DatabaseScopedCredentials);

        if (!compatibility.IsAllowed)
        {
            throw new DatabaseCompatibilityException(compatibility);
        }

        if (mode == MigratorMode.SchemaOnly)
        {
            Console.WriteLine("Applying schema migrations only...");
            await _migrationExecutor.MigrateAsync(ct);
            var verified = await _databasePreflight.InspectAsync(ct);
            EnsureCurrentSchema(verified);
            Console.WriteLine("SCHEMA_ONLY_VERIFIED; SourceMigrations={0}; AppliedMigrations={1}",
                verified.SourceMigrationCount, verified.AppliedMigrationCount);
            return;
        }

        // Explicit data operations never apply migrations, even to an empty database.
        EnsureCurrentSchema(compatibility);
        ProductionBootstrapPlan? initialBootstrapPlan = null;
        if (mode == MigratorMode.Bootstrap)
        {
            initialBootstrapPlan = await _productionBootstrapper.InspectAsync(ct);
            EnsureBootstrapPlanIsAccepted(initialBootstrapPlan);
        }

        await _transactionRunner.ExecuteAsync(async transactionCt =>
        {
            switch (mode)
            {
                case MigratorMode.SecuritySeed:
                    await _mandatorySecuritySeeder.SeedAsync(transactionCt);
                    break;
                case MigratorMode.DemoSeed:
                    await _demoDataSeeder.SeedAsync(_seedOptions, transactionCt);
                    break;
                case MigratorMode.Bootstrap:
                    var lockedPlan = await _productionBootstrapper.InspectAsync(transactionCt);
                    EnsureBootstrapPlanIsAccepted(lockedPlan);
                    if (lockedPlan.State != initialBootstrapPlan!.State)
                        throw new ProductionBootstrapStateException("BootstrapStateChanged");
                    // Matching bootstrap is a true no-op: do not reconcile customized menus/roles.
                    await _productionBootstrapper.ApplyAsync(lockedPlan, transactionCt);
                    break;
                default:
                    throw new MigratorConfigurationException(MigratorCommand.Usage);
            }
        }, ct);
        Console.WriteLine("Explicit operation completed: {0}.", mode);
    }

    private static void EnsureCurrentSchema(DatabaseCompatibilityResult result)
    {
        if (!result.IsAllowed || result.State != DatabaseCompatibilityState.CurrentBaseline
            || result.AppliedMigrationCount != result.SourceMigrationCount)
            throw new DatabaseCompatibilityException(result with
            {
                IsAllowed = false,
                SafeReasonCode = "CurrentSchemaRequired"
            });
    }

    private static void EnsureBootstrapPlanIsAccepted(
        ProductionBootstrapPlan plan)
    {
        if (plan.State
            is ProductionBootstrapPlanState.PartialOrDifferent)
        {
            throw new ProductionBootstrapStateException(
                "PartialOrDifferent");
        }

        if (plan.State is ProductionBootstrapPlanState.Disabled)
        {
            throw new ProductionBootstrapStateException(
                "UnexpectedDisabledPlan");
        }
    }
}
