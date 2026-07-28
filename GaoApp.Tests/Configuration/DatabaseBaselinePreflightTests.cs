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
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class DatabaseBaselinePreflightTests
{
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
    public async Task Applied_baseline_with_missing_core_table_should_be_rejected()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var baselineId = db.Database.GetMigrations().Single();
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
        after.Should().ContainSingle();
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
    private const string Prefix = "GaoApp_R1Final_Preflight_";
    private const string TestDataSourceEnvironmentVariable =
        "GAOAPP_R1_FINAL_TEST_SQL_SERVER";
    private static readonly Regex SafeNamePattern = new(
        "^GaoApp_R1Final_Preflight_[A-F0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly string _databaseName =
        $"{Prefix}{Guid.NewGuid():N}".ToUpperInvariant();
    private bool _disposed;

    public string ConnectionString
        => new SqlConnectionStringBuilder
        {
            DataSource = GetTestDataSource(),
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            MultipleActiveResultSets = true
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
        GuardDatabaseName();
        await using var connection = new SqlConnection(
            CreateMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{_databaseName}];";
        await command.ExecuteNonQueryAsync();
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

        _disposed = true;
        GuardDatabaseName();

        await using var connection = new SqlConnection(
            CreateMasterConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE [{_databaseName}]
                    SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{_databaseName}];
            END;
            """;
        command.Parameters.AddWithValue(
            "@databaseName",
            _databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private void GuardDatabaseName()
    {
        if (!SafeNamePattern.IsMatch(_databaseName)
            || !_databaseName.StartsWith(
                Prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to operate on a database outside the acceptance prefix.");
        }
    }

    private static string CreateMasterConnectionString()
        => new SqlConnectionStringBuilder
        {
            DataSource = GetTestDataSource(),
            InitialCatalog = "master",
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15
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
