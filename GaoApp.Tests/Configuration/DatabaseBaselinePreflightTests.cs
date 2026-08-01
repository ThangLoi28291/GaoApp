using System.Data;
using System.Text.RegularExpressions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Infrastructure.Tenant;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseBaselinePreflightTests
{
    private const string BaselineMigrationId =
        "20260726073029_InitialProductionBaseline";

    private const string InventoryPostingMigrationId =
        "20260801110856_AddInventoryPostingIdempotency";

    [Fact]
    public async Task Missing_database_should_be_allowed_and_migrate_successfully()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var preflight = CreatePreflight(db);

        var before = await preflight.InspectAsync();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var after = await preflight.InspectAsync();

        before.State.Should().Be(
            DatabaseCompatibilityState.DatabaseMissing);
        before.IsAllowed.Should().BeTrue();
        after.State.Should().Be(
            DatabaseCompatibilityState.CurrentBaseline);
        after.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_empty_database_should_be_allowed()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.State.Should().Be(
            DatabaseCompatibilityState.ExistingEmpty);
        result.IsAllowed.Should().BeTrue();
        result.AppliedMigrationCount.Should().Be(0);
        result.UserTableCount.Should().Be(0);
    }

    [Fact]
    public async Task Existing_database_with_canary_table_should_be_rejected_before_ddl()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[AcceptanceCanary]
            (
                [Id] int NOT NULL PRIMARY KEY,
                [Value] nvarchar(100) NOT NULL
            );
            INSERT INTO [dbo].[AcceptanceCanary] ([Id], [Value])
            VALUES (1, N'unchanged');
            """);
        var before = await database.ReadScalarAsync<string>("""
            SELECT CONCAT(
                COUNT_BIG(*),
                N':',
                COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM([Id], [Value])), 0))
            FROM [dbo].[AcceptanceCanary];
            """);

        await using var db = database.CreateContext();
        var migration = new CountingMigrationExecutor();
        var mandatory = new CountingMandatorySeeder();
        var demo = new CountingDemoSeeder();
        var bootstrap = new CountingBootstrapper();
        var pipeline = new MigrationExecutionPipeline(
            Options.Create(new SeedDataOptions()),
            Options.Create(new ProductionBootstrapOptions()),
            new TestHostEnvironment(Environments.Production),
            new MigratorConfigurationValidator(),
            CreatePreflight(db),
            migration,
            mandatory,
            demo,
            bootstrap,
            new EfCoreProvisioningTransactionRunner(db));

        var action = () => pipeline.RunAsync();

        await action.Should()
            .ThrowAsync<DatabaseCompatibilityException>();
        migration.Count.Should().Be(0);
        mandatory.Count.Should().Be(0);
        demo.Count.Should().Be(0);
        bootstrap.Count.Should().Be(0);

        var after = await database.ReadScalarAsync<string>("""
            SELECT CONCAT(
                COUNT_BIG(*),
                N':',
                COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM([Id], [Value])), 0))
            FROM [dbo].[AcceptanceCanary];
            """);
        after.Should().Be(before);
        (await database.ObjectExistsAsync("dbo", "__EFMigrationsHistory"))
            .Should().BeFalse();
        (await database.ObjectExistsAsync("dbo", "Stores"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Existing_database_with_unknown_migration_history_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.CreateMigrationHistoryAsync(
            "20000101000000_SyntheticLegacyMigration");
        var before = await database.ReadMigrationHistoryAsync();
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.State.Should().Be(
            DatabaseCompatibilityState.UnknownMigrationHistory);
        result.IsAllowed.Should().BeFalse();
        (await database.ReadMigrationHistoryAsync())
            .Should().Equal(before);
        (await database.ObjectExistsAsync("dbo", "Stores"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Existing_schema_without_history_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[LegacyApplicationTable]
            (
                [Id] int NOT NULL PRIMARY KEY
            );
            """);
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.State.Should().Be(
            DatabaseCompatibilityState.ExistingSchemaWithoutHistory);
        result.IsAllowed.Should().BeFalse();
        (await database.ObjectExistsAsync("dbo", "__EFMigrationsHistory"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Exact_baseline_target_should_not_apply_inventory_posting_migration()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var baselineMatches = db.Database.GetMigrations()
            .Where(x => string.Equals(
                x,
                BaselineMigrationId,
                StringComparison.Ordinal))
            .ToList();
        baselineMatches.Should().ContainSingle(
            $"migration {BaselineMigrationId} must exist exactly once");
        var baselineId = baselineMatches.Single();

        await db.GetService<IMigrator>()
            .MigrateAsync(baselineId);

        var applied = await database.ReadMigrationHistoryAsync();
        applied.Should().Equal(BaselineMigrationId);
        applied.Should().NotContain(InventoryPostingMigrationId);
    }

    [Fact]
    public async Task Applied_baseline_with_missing_core_table_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var baselineMatches = db.Database.GetMigrations()
            .Where(x => string.Equals(
                x,
                BaselineMigrationId,
                StringComparison.Ordinal))
            .ToList();
        baselineMatches.Should().ContainSingle(
            $"migration {BaselineMigrationId} must exist exactly once");
        var baselineId = baselineMatches.Single();
        await database.CreateMigrationHistoryAsync(baselineId);

        var result = await CreatePreflight(db).InspectAsync();

        result.State.Should().Be(
            DatabaseCompatibilityState.PartialOrCorruptBaseline);
        result.IsAllowed.Should().BeFalse();
        result.SafeReasonCode.Should().Be(
            "StructuralSchemaMismatch");
    }

    [Fact]
    public async Task Current_baseline_database_should_allow_no_op_rerun()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var migrationExecutor = new EfCoreDatabaseMigrationExecutor(db);
        await migrationExecutor.MigrateAsync();
        var before = await database.ReadMigrationHistoryAsync();

        var preflight = await CreatePreflight(db).InspectAsync();
        await migrationExecutor.MigrateAsync();
        var after = await database.ReadMigrationHistoryAsync();

        preflight.State.Should().Be(
            DatabaseCompatibilityState.CurrentBaseline);
        preflight.IsAllowed.Should().BeTrue();
        after.Should().Equal(before);
        after.Should().BeEquivalentTo(
            [
                BaselineMigrationId,
                InventoryPostingMigrationId
            ]);
    }

    [Fact]
    public async Task Current_baseline_with_matching_bootstrap_should_be_idempotent()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var options = CreateValidBootstrapOptions();
        var bootstrapper = new ProductionBootstrapper(
            db,
            Options.Create(options));

        var transactionRunner =
            new EfCoreProvisioningTransactionRunner(db);
        ProductionBootstrapResult? first = null;
        ProductionBootstrapResult? second = null;
        await transactionRunner.ExecuteAsync(async ct =>
        {
            var plan = await bootstrapper.InspectAsync(ct);
            first = await bootstrapper.ApplyAsync(plan, ct);
        });
        await transactionRunner.ExecuteAsync(async ct =>
        {
            var plan = await bootstrapper.InspectAsync(ct);
            second = await bootstrapper.ApplyAsync(plan, ct);
        });

        first.Should().Be(ProductionBootstrapResult.Created);
        second.Should().Be(ProductionBootstrapResult.AlreadyProvisioned);
        (await db.Stores.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Current_baseline_with_unknown_extra_user_table_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using (var migrationContext = database.CreateContext())
        {
            await new EfCoreDatabaseMigrationExecutor(migrationContext)
                .MigrateAsync();
        }

        await database.ExecuteAsync("""
            CREATE TABLE [dbo].[ForeignApplicationCanary]
            (
                [Id] int NOT NULL PRIMARY KEY
            );
            """);
        await using var db = database.CreateContext();

        var result = await CreatePreflight(db).InspectAsync();

        result.State.Should().Be(
            DatabaseCompatibilityState.PartialOrCorruptBaseline);
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Caller_cancellation_should_propagate_from_preflight()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = () => CreatePreflight(db)
            .InspectAsync(cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Supported_history_prefix_should_be_allowed_by_catalog_contract()
    {
        var catalog = new DatabaseMigrationCatalogSnapshot(
            ["M001", "M002"]);

        catalog.IsAppliedHistoryPrefix(["M001"]).Should().BeTrue();
        catalog.IsAppliedHistoryPrefix(["M002"]).Should().BeFalse();
        catalog.IsAppliedHistoryPrefix(["M001", "Unknown"])
            .Should().BeFalse();
    }

    [Fact]
    public async Task Production_bootstrap_failure_should_not_leave_partial_admin_fixture()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using (var migrationContext = database.CreateContext())
        {
            await new EfCoreDatabaseMigrationExecutor(migrationContext)
                .MigrateAsync();
        }

        var interceptor = new FailOnSaveChangesInterceptor(failOnCall: 3);
        await using (var failingContext =
                     database.CreateContext(interceptor))
        {
            var bootstrapper = new ProductionBootstrapper(
                failingContext,
                Options.Create(CreateValidBootstrapOptions()));

            var transactionRunner =
                new EfCoreProvisioningTransactionRunner(failingContext);
            var action = () => transactionRunner.ExecuteAsync(
                async ct =>
                {
                    var plan = await bootstrapper.InspectAsync(ct);
                    await bootstrapper.ApplyAsync(plan, ct);
                });

            await action.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("Synthetic bootstrap save failure.");
        }

        await using var verificationContext = database.CreateContext();
        (await verificationContext.Stores
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(0);
        (await verificationContext.Users
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(0);
        (await verificationContext.Warehouses
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(0);
        (await verificationContext.POSTerminals
                .IgnoreQueryFilters()
                .CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Preflight_failure_should_not_expose_connection_or_credentials()
    {
        var marker = "Sensitive-Connection-Marker";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                $"Server=127.0.0.1,1;Database={marker};User Id=synthetic;Password={marker};Encrypt=False;Connect Timeout=1")
            .Options;
        var tenant = new TenantContext();
        tenant.SetHostAdmin();
        await using var db = new AppDbContext(
            options,
            tenant,
            new PreflightCurrentUser());

        var result = await CreatePreflight(db).InspectAsync();
        var exception = new DatabaseCompatibilityException(result);

        result.State.Should().Be(
            DatabaseCompatibilityState.Inaccessible);
        exception.ToString().Should()
            .NotContain(marker)
            .And.NotContain("127.0.0.1")
            .And.NotContain("Password=");
    }

    [Fact]
    public async Task CreateDatabaseAsync_should_create_database_and_verify_DB_ID()
    {
        var database = new PreflightAcceptanceDatabase();

        try
        {
            await database.CreateDatabaseAsync();

            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();
            (await ReadDatabaseStateIndependentlyAsync(database))
                .Should().Be("ONLINE");
            (await ProbeTargetDatabaseIndependentlyAsync(database))
                .Should().Be(1);
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public async Task CreateDatabaseAsync_when_command_reports_timeout_but_database_exists_should_succeed()
    {
        var database = new PreflightAcceptanceDatabase();
        var seamCallCount = 0;
        SqlException? callbackTimeout = null;
        database.CreateDatabaseCommandAsyncOverride =
            async (connection, databaseName) =>
            {
                seamCallCount++;
                await CreateDatabaseIndependentlyAsync(
                    database,
                    databaseName);
                (await DatabaseExistsIndependentlyAsync(database))
                    .Should().BeTrue();
                (await ReadDatabaseStateIndependentlyAsync(database))
                    .Should().Be("ONLINE");
                (await ProbeTargetDatabaseIndependentlyAsync(database))
                    .Should().Be(1);

                try
                {
                    await ExecuteDeterministicCommandTimeoutAsync(
                        connection);
                }
                catch (SqlException timeoutException)
                    when (timeoutException.Number == -2)
                {
                    callbackTimeout = timeoutException;
                    throw;
                }
            };

        try
        {
            await database.CreateDatabaseAsync();

            seamCallCount.Should().Be(1);
            callbackTimeout.Should().NotBeNull();
            callbackTimeout!.Number.Should().Be(-2);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();
            (await ReadDatabaseStateIndependentlyAsync(database))
                .Should().Be("ONLINE");
            (await ProbeTargetDatabaseIndependentlyAsync(database))
                .Should().Be(1);

            await database.DisposeAsync();

            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public async Task CreateDatabaseAsync_when_command_times_out_and_database_does_not_exist_should_rethrow_original_exception()
    {
        var database = new PreflightAcceptanceDatabase();
        var seamCallCount = 0;
        SqlException? callbackTimeout = null;
        database.CreateDatabaseCommandAsyncOverride =
            async (connection, _) =>
            {
                seamCallCount++;

                try
                {
                    await ExecuteDeterministicCommandTimeoutAsync(
                        connection);
                }
                catch (SqlException timeoutException)
                    when (timeoutException.Number == -2)
                {
                    callbackTimeout = timeoutException;
                    throw;
                }
            };

        try
        {
            var action = () => database.CreateDatabaseAsync();

            var exception = await action.Should()
                .ThrowAsync<SqlException>();

            seamCallCount.Should().Be(1);
            callbackTimeout.Should().NotBeNull();
            exception.Which.Should().BeSameAs(callbackTimeout);
            exception.Which.Number.Should().Be(-2);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();

            await database.DisposeAsync();

            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public async Task CreateDatabaseAsync_when_normal_create_readiness_is_false_should_fail()
    {
        var database = new PreflightAcceptanceDatabase();
        var readinessCallCount = 0;
        database.DatabaseReadinessAsyncOverride = () =>
        {
            readinessCallCount++;
            return Task.FromResult(false);
        };

        try
        {
            var action = () => database.CreateDatabaseAsync();

            await action.Should()
                .ThrowAsync<InvalidOperationException>();

            readinessCallCount.Should().Be(1);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task CreateDatabaseAsync_when_timeout_database_exists_but_readiness_is_false_should_rethrow_original_exception()
    {
        var database = new PreflightAcceptanceDatabase();
        var seamCallCount = 0;
        var readinessCallCount = 0;
        SqlException? callbackTimeout = null;
        database.CreateDatabaseCommandAsyncOverride =
            async (connection, databaseName) =>
            {
                seamCallCount++;
                await CreateDatabaseIndependentlyAsync(
                    database,
                    databaseName);
                (await DatabaseExistsIndependentlyAsync(database))
                    .Should().BeTrue();

                try
                {
                    await ExecuteDeterministicCommandTimeoutAsync(
                        connection);
                }
                catch (SqlException timeoutException)
                    when (timeoutException.Number == -2)
                {
                    callbackTimeout = timeoutException;
                    throw;
                }
            };
        database.DatabaseReadinessAsyncOverride = () =>
        {
            readinessCallCount++;
            return Task.FromResult(false);
        };

        try
        {
            var action = () => database.CreateDatabaseAsync();

            var exception = await action.Should()
                .ThrowAsync<SqlException>();

            seamCallCount.Should().Be(1);
            callbackTimeout.Should().NotBeNull();
            exception.Which.Should().BeSameAs(callbackTimeout);
            exception.Which.Number.Should().Be(-2);
            readinessCallCount.Should().Be(1);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();

            await database.DisposeAsync();

            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
    }

    [Fact]
    public async Task DisposeAsync_when_database_already_absent_should_succeed()
    {
        var database = new PreflightAcceptanceDatabase();

        try
        {
            await database.CreateDatabaseAsync();
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();
            await DropDatabaseIndependentlyAsync(database);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();

            var action = () => database.DisposeAsync().AsTask();

            await action.Should().NotThrowAsync();
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public async Task DisposeAsync_should_be_idempotent()
    {
        var database = new PreflightAcceptanceDatabase();

        try
        {
            await database.CreateDatabaseAsync();
            (await database.ReadScalarAsync<int>("SELECT 1;"))
                .Should().Be(1);

            var firstDispose = () => database.DisposeAsync().AsTask();
            var secondDispose = () => database.DisposeAsync().AsTask();

            await firstDispose.Should().NotThrowAsync();
            await secondDispose.Should().NotThrowAsync();
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public async Task DisposeAsync_when_database_disappears_after_precheck_should_succeed()
    {
        var database = new PreflightAcceptanceDatabase();
        var seamCallCount = 0;

        try
        {
            await database.CreateDatabaseAsync();
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeTrue();
            database.BeforeCleanupCommandAsync = async () =>
            {
                seamCallCount++;
                await DropDatabaseIndependentlyAsync(database);
                (await DatabaseExistsIndependentlyAsync(database))
                    .Should().BeFalse();
            };

            var firstDispose = () => database.DisposeAsync().AsTask();

            await firstDispose.Should().NotThrowAsync();
            seamCallCount.Should().Be(1);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();

            var secondDispose = () => database.DisposeAsync().AsTask();

            await secondDispose.Should().NotThrowAsync();
            seamCallCount.Should().Be(1);
            (await DatabaseExistsIndependentlyAsync(database))
                .Should().BeFalse();
        }
        finally
        {
            await DropDatabaseIndependentlyAsync(database);
        }
    }

    [Fact]
    public void Database_name_guard_should_reject_name_outside_acceptance_prefix()
    {
        var action = () =>
            PreflightAcceptanceDatabase.GuardDatabaseName(
                "GaoApp_Production");

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Refusing to operate on a database outside the acceptance prefix.");
    }

    private static async Task CreateDatabaseIndependentlyAsync(
        PreflightAcceptanceDatabase database,
        string databaseName)
    {
        PreflightAcceptanceDatabase.GuardDatabaseName(databaseName);
        await using var connection = new SqlConnection(
            CreateIndependentMasterConnectionString(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}];";
        command.CommandTimeout =
            PreflightAcceptanceDatabase
                .CreateDatabaseCommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ReadDatabaseStateIndependentlyAsync(
        PreflightAcceptanceDatabase database)
    {
        var databaseName = new SqlConnectionStringBuilder(
            database.ConnectionString).InitialCatalog;
        PreflightAcceptanceDatabase.GuardDatabaseName(databaseName);
        await using var connection = new SqlConnection(
            CreateIndependentMasterConnectionString(database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT state_desc
            FROM sys.databases
            WHERE name = @databaseName;
            """;
        command.CommandTimeout =
            PreflightAcceptanceDatabase
                .CreateDatabaseCommandTimeoutSeconds;
        command.Parameters.Add(
            new SqlParameter(
                "@databaseName",
                SqlDbType.NVarChar,
                128)
            {
                Value = databaseName
            });
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<int> ProbeTargetDatabaseIndependentlyAsync(
        PreflightAcceptanceDatabase database)
    {
        var builder = new SqlConnectionStringBuilder(
            database.ConnectionString)
        {
            Pooling = false
        };
        PreflightAcceptanceDatabase.GuardDatabaseName(
            builder.InitialCatalog);
        await using var connection = new SqlConnection(
            builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        command.CommandTimeout =
            PreflightAcceptanceDatabase
                .CreateDatabaseCommandTimeoutSeconds;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteDeterministicCommandTimeoutAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "WAITFOR DELAY '00:00:02';";
        command.CommandTimeout = 1;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> DatabaseExistsIndependentlyAsync(
        PreflightAcceptanceDatabase database)
    {
        var databaseName = new SqlConnectionStringBuilder(
            database.ConnectionString).InitialCatalog;
        PreflightAcceptanceDatabase.GuardDatabaseName(databaseName);
        await using var connection = new SqlConnection(
            CreateIndependentMasterConnectionString(database));
        await connection.OpenAsync();
        return await DatabaseExistsIndependentlyAsync(
            connection,
            databaseName);
    }

    private static async Task<bool> DatabaseExistsIndependentlyAsync(
        SqlConnection connection,
        string databaseName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_ID(@databaseName);";
        command.Parameters.Add(
            new SqlParameter(
                "@databaseName",
                SqlDbType.NVarChar,
                128)
            {
                Value = databaseName
            });
        var result = await command.ExecuteScalarAsync();
        return result is not null and not DBNull;
    }

    private static async Task DropDatabaseIndependentlyAsync(
        PreflightAcceptanceDatabase database)
    {
        var databaseName = new SqlConnectionStringBuilder(
            database.ConnectionString).InitialCatalog;
        PreflightAcceptanceDatabase.GuardDatabaseName(databaseName);
        await using var connection = new SqlConnection(
            CreateIndependentMasterConnectionString(database));
        await connection.OpenAsync();

        if (!await DatabaseExistsIndependentlyAsync(
                connection,
                databaseName))
        {
            return;
        }

        try
        {
            await using (var alterCommand = connection.CreateCommand())
            {
                alterCommand.CommandText = $"""
                    ALTER DATABASE [{databaseName}]
                        SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    """;
                await alterCommand.ExecuteNonQueryAsync();
            }

            await using var dropCommand = connection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE [{databaseName}];";
            await dropCommand.ExecuteNonQueryAsync();
        }
        catch (SqlException cleanupException)
        {
            bool databaseExists;

            try
            {
                databaseExists =
                    await DatabaseExistsIndependentlyAsync(database);
            }
            catch (Exception verificationException)
            {
                throw new AggregateException(
                    "Independent database cleanup failed and the cleanup postcondition could not be verified.",
                    cleanupException,
                    verificationException);
            }

            if (!databaseExists)
            {
                return;
            }

            throw;
        }

        if (await DatabaseExistsIndependentlyAsync(
                connection,
                databaseName))
        {
            throw new InvalidOperationException(
                "Independent cleanup completed but the acceptance database still exists.");
        }
    }

    private static string CreateIndependentMasterConnectionString(
        PreflightAcceptanceDatabase database)
    {
        var builder = new SqlConnectionStringBuilder(
            database.ConnectionString)
        {
            InitialCatalog = "master",
            MultipleActiveResultSets = false,
            Pooling = false
        };
        return builder.ConnectionString;
    }

    private static SqlServerDatabaseBaselinePreflight CreatePreflight(
        AppDbContext db)
        => new(
            db,
            new EfCoreDatabaseMigrationCatalog(db),
            new SqlServerDatabaseObjectInventoryReader(db),
            new EfCoreDatabaseSchemaManifestCatalog(db),
            new SqlServerSchemaSnapshotReader(db));

    private static ProductionBootstrapOptions CreateValidBootstrapOptions()
        => new()
        {
            Enabled = true,
            StoreName = "Acceptance Store",
            StoreSubdomain = "acceptance-store",
            LegalEntityCode = "LEGAL-01",
            LegalEntityName = "Acceptance Legal Entity",
            LegalEntityLegalName = "Acceptance Legal Entity Limited",
            WarehouseCode = "WAREHOUSE-01",
            WarehouseName = "Acceptance Warehouse",
            TerminalCode = "POS-01",
            TerminalName = "Acceptance POS",
            AdminUserName = "acceptance.admin",
            AdminFullName = "Acceptance Administrator",
            AdminEmail = "acceptance.admin@example.invalid",
            AdminPassword = "Synthetic-Bootstrap-Test-42!"
        };

    private sealed class FailOnSaveChangesInterceptor
        : SaveChangesInterceptor
    {
        private readonly int _failOnCall;
        private int _callCount;

        public FailOnSaveChangesInterceptor(int failOnCall)
        {
            _failOnCall = failOnCall;
        }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            _callCount++;

            if (_callCount == _failOnCall)
            {
                throw new InvalidOperationException(
                    "Synthetic bootstrap save failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

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
            return Task.FromResult(ProductionBootstrapResult.Created);
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

[CollectionDefinition(
    "R1FinalDatabasePreflight",
    DisableParallelization = true)]
public sealed class R1FinalDatabasePreflightCollection;

internal sealed class PreflightAcceptanceDatabase : IAsyncDisposable
{
    internal const int CreateDatabaseCommandTimeoutSeconds = 60;

    private const string Prefix = "GaoApp_R1Final_Preflight_";
    private const string TestDataSourceEnvironmentVariable =
        "GAOAPP_R1_FINAL_TEST_SQL_SERVER";
    private static readonly Regex SafeNamePattern = new(
        "^GaoApp_R1Final_Preflight_[A-F0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string _databaseName =
        $"{Prefix}{Guid.NewGuid():N}".ToUpperInvariant();
    private bool _disposed;

    internal Func<Task>? BeforeCleanupCommandAsync { get; set; }
    internal Func<SqlConnection, string, Task>?
        CreateDatabaseCommandAsyncOverride { get; set; }
    internal Func<Task<bool>>?
        DatabaseReadinessAsyncOverride { get; set; }

    public string ConnectionString
        => new SqlConnectionStringBuilder
        {
            DataSource = GetTestDataSource(),
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            MultipleActiveResultSets = true,
            Pooling = false
        }.ConnectionString;

    public AppDbContext CreateContext(
        IInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString);

        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        var tenant = new TenantContext();
        tenant.SetHostAdmin();

        return new AppDbContext(
            optionsBuilder.Options,
            tenant,
            new PreflightCurrentUser());
    }

    public async Task CreateDatabaseAsync()
    {
        GuardDatabaseName(_databaseName);
        await using var connection = new SqlConnection(
            CreateMasterConnectionString());
        await connection.OpenAsync();

        try
        {
            await ExecuteCreateDatabaseCommandAsync(connection);
        }
        catch (SqlException timeoutException)
            when (timeoutException.Number == -2)
        {
            try
            {
                if (await EvaluateDatabaseReadinessAsync())
                {
                    return;
                }
            }
            catch (Exception verificationException)
            {
                throw new AggregateException(
                    "CREATE DATABASE timed out and readiness verification also failed.",
                    timeoutException,
                    verificationException);
            }

            throw;
        }

        bool databaseIsReady;

        try
        {
            databaseIsReady =
                await EvaluateDatabaseReadinessAsync();
        }
        catch (Exception verificationException)
        {
            throw new InvalidOperationException(
                "CREATE DATABASE completed but readiness verification failed.",
                verificationException);
        }

        if (!databaseIsReady)
        {
            throw new InvalidOperationException(
                "CREATE DATABASE completed but the acceptance database is not ONLINE and usable.");
        }
    }

    private async Task ExecuteCreateDatabaseCommandAsync(
        SqlConnection connection)
    {
        if (CreateDatabaseCommandAsyncOverride is not null)
        {
            await CreateDatabaseCommandAsyncOverride(
                connection,
                _databaseName);
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_databaseName}];";
        command.CommandTimeout =
            CreateDatabaseCommandTimeoutSeconds;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<bool> DatabaseIsReadyUsingNewConnectionsAsync()
    {
        GuardDatabaseName(_databaseName);
        await using var masterConnection = new SqlConnection(
            CreateMasterConnectionString());
        await masterConnection.OpenAsync();
        await using var stateCommand =
            masterConnection.CreateCommand();
        stateCommand.CommandText = """
            SELECT state_desc
            FROM sys.databases
            WHERE name = @databaseName;
            """;
        stateCommand.CommandTimeout =
            CreateDatabaseCommandTimeoutSeconds;
        stateCommand.Parameters.Add(
            new SqlParameter(
                "@databaseName",
                SqlDbType.NVarChar,
                128)
            {
                Value = _databaseName
            });
        var state = await stateCommand.ExecuteScalarAsync() as string;

        if (!string.Equals(
                state,
                "ONLINE",
                StringComparison.Ordinal))
        {
            return false;
        }

        await using var targetConnection =
            new SqlConnection(ConnectionString);
        await targetConnection.OpenAsync();
        await using var probeCommand =
            targetConnection.CreateCommand();
        probeCommand.CommandText = "SELECT 1;";
        probeCommand.CommandTimeout =
            CreateDatabaseCommandTimeoutSeconds;
        var probeResult = await probeCommand.ExecuteScalarAsync();
        return Convert.ToInt32(probeResult) == 1;
    }

    private Task<bool> EvaluateDatabaseReadinessAsync()
    {
        var readinessOverride = DatabaseReadinessAsyncOverride;
        return readinessOverride is null
            ? DatabaseIsReadyUsingNewConnectionsAsync()
            : readinessOverride();
    }

    public async Task ExecuteAsync(
        string sql,
        params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> ReadScalarAsync<T>(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    public Task<string> ReadDatabaseObjectSignatureAsync()
        => ReadScalarAsync<string>("""
            SELECT CONCAT(
                (
                    SELECT COUNT_BIG(*)
                    FROM [sys].[objects] AS [object]
                    WHERE [object].[is_ms_shipped] = 0
                ),
                N':',
                (
                    SELECT COALESCE(
                        CHECKSUM_AGG(
                            BINARY_CHECKSUM(
                                [object].[type],
                                [object].[schema_id],
                                [object].[name],
                                OBJECT_DEFINITION([object].[object_id]))),
                        0)
                    FROM [sys].[objects] AS [object]
                    WHERE [object].[is_ms_shipped] = 0
                ),
                N':',
                (
                    SELECT COUNT_BIG(*)
                    FROM [sys].[schemas] AS [schema]
                    WHERE [schema].[name] NOT IN
                    (
                        N'dbo', N'guest', N'sys',
                        N'INFORMATION_SCHEMA',
                        N'db_owner', N'db_accessadmin',
                        N'db_securityadmin', N'db_ddladmin',
                        N'db_backupoperator', N'db_datareader',
                        N'db_datawriter', N'db_denydatareader',
                        N'db_denydatawriter'
                    )
                ),
                N':',
                (
                    SELECT COUNT_BIG(*)
                    FROM [sys].[types] AS [type]
                    WHERE [type].[is_user_defined] = 1
                ));
            """);

    public Task<string> ReadProvisioningStateSignatureAsync()
        => ReadScalarAsync<string>("""
            SELECT CONCAT(
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [Code], [Name], [GroupName])), 0))
                 FROM [dbo].[Permissions]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [ParentId], [Title],
                        [PermissionCode], [SortOrder],
                        [IsActive], [IsSystem], [IsDeleted])), 0))
                 FROM [dbo].[AdminMenuItems]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [Code], [Name],
                        [IsSystemRole], [IsDeleted])), 0))
                 FROM [dbo].[Roles]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [Name], [SubDomainNormalized],
                        [IsActive], [IsDeleted])), 0))
                 FROM [dbo].[Stores]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [UserName], [FullName], [Email],
                        [PasswordHash], [IsActive], [IsHostAdmin],
                        [IsDeleted])), 0))
                 FROM [dbo].[Users]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [UserId], [RoleId],
                        [IsActive], [IsDeleted])), 0))
                 FROM [dbo].[UserInStores]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [LegalEntityId], [Code],
                        [Name], [IsDefault], [IsActive],
                        [IsDeleted])), 0))
                 FROM [dbo].[Warehouses]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [Code], [Name],
                        [IsActive], [IsDeleted])), 0))
                 FROM [dbo].[POSTerminals]),
                N'|',
                (SELECT CONCAT(
                    COUNT_BIG(*), N':',
                    COALESCE(CHECKSUM_AGG(BINARY_CHECKSUM(
                        [Id], [StoreId], [Code], [Name],
                        [LegalName], [DefaultWarehouseId],
                        [IsDefaultForPurchase], [IsActive],
                        [IsDeleted])), 0))
                 FROM [dbo].[LegalEntities]));
            """);

    public async Task<bool> ObjectExistsAsync(
        string schema,
        string table)
    {
        const string sql = """
            SELECT COUNT_BIG(*)
            FROM [sys].[tables] AS [table]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            WHERE [schema].[name] = @schema
              AND [table].[name] = @table;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        return (long)(await command.ExecuteScalarAsync())! > 0;
    }

    public async Task CreateMigrationHistoryAsync(string migrationId)
    {
        await ExecuteAsync(
            """
            CREATE TABLE [dbo].[__EFMigrationsHistory]
            (
                [MigrationId] nvarchar(150) NOT NULL,
                [ProductVersion] nvarchar(32) NOT NULL,
                CONSTRAINT [PK___EFMigrationsHistory]
                    PRIMARY KEY ([MigrationId])
            );
            INSERT INTO [dbo].[__EFMigrationsHistory]
                ([MigrationId], [ProductVersion])
            VALUES (@migrationId, N'8.0.29');
            """,
            new SqlParameter("@migrationId", SqlDbType.NVarChar, 150)
            {
                Value = migrationId
            });
    }

    public async Task<IReadOnlyList<string>> ReadMigrationHistoryAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT [MigrationId]
            FROM [dbo].[__EFMigrationsHistory]
            ORDER BY [MigrationId];
            """;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        GuardDatabaseName(_databaseName);

        await using var connection = new SqlConnection(
            CreateMasterConnectionString());
        await connection.OpenAsync();

        if (!await DatabaseExistsAsync(connection))
        {
            _disposed = true;
            return;
        }

        if (BeforeCleanupCommandAsync is not null)
        {
            await BeforeCleanupCommandAsync();
        }

        try
        {
            await ExecuteMasterCommandAsync(
                connection,
                $"""
                ALTER DATABASE [{_databaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                """);
            await ExecuteMasterCommandAsync(
                connection,
                $"DROP DATABASE [{_databaseName}];");
        }
        catch (SqlException cleanupException)
        {
            bool databaseExists;

            try
            {
                databaseExists =
                    await DatabaseExistsUsingNewConnectionAsync();
            }
            catch (Exception verificationException)
            {
                throw new AggregateException(
                    "Database cleanup failed and the cleanup postcondition could not be verified.",
                    cleanupException,
                    verificationException);
            }

            if (!databaseExists)
            {
                _disposed = true;
                return;
            }

            throw;
        }

        if (await DatabaseExistsAsync(connection))
        {
            throw new InvalidOperationException(
                "Database cleanup completed but the acceptance database still exists.");
        }

        _disposed = true;
    }

    internal static void GuardDatabaseName(string databaseName)
    {
        if (!SafeNamePattern.IsMatch(databaseName)
            || !databaseName.StartsWith(
                Prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to operate on a database outside the acceptance prefix.");
        }
    }

    private async Task<bool> DatabaseExistsUsingNewConnectionAsync()
    {
        await using var connection = new SqlConnection(
            CreateMasterConnectionString());
        await connection.OpenAsync();
        return await DatabaseExistsAsync(connection);
    }

    private async Task<bool> DatabaseExistsAsync(
        SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_ID(@databaseName);";
        command.Parameters.Add(
            new SqlParameter(
                "@databaseName",
                SqlDbType.NVarChar,
                128)
            {
                Value = _databaseName
            });
        var result = await command.ExecuteScalarAsync();
        return result is not null and not DBNull;
    }

    private static async Task ExecuteMasterCommandAsync(
        SqlConnection connection,
        string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateMasterConnectionString()
        => new SqlConnectionStringBuilder
        {
            DataSource = GetTestDataSource(),
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            Pooling = false
        }.ConnectionString;

    private static string GetTestDataSource()
    {
        var configured = Environment.GetEnvironmentVariable(
            TestDataSourceEnvironmentVariable);
        var dataSource = string.IsNullOrWhiteSpace(configured)
            ? @"(localdb)\MSSQLLocalDB"
            : configured.Trim();

        if (!dataSource.StartsWith(
                @"(localdb)\",
                StringComparison.OrdinalIgnoreCase)
            || dataSource.Length <= @"(localdb)\".Length)
        {
            throw new InvalidOperationException(
                $"{TestDataSourceEnvironmentVariable} must identify a named LocalDB instance.");
        }

        return dataSource;
    }
}

internal sealed class PreflightCurrentUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
