using System.Data;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

internal sealed class InventoryPostingLocalDb : IAsyncDisposable
{
    private static string DataSource => SqlTestDataSource.Current;
    private const string Prefix = "GaoApp_R2_InventoryPosting_";
    private const int ConnectionTimeoutSeconds = 30;

    private static readonly Regex SafeDatabaseName = new(
        "^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$",
        RegexOptions.CultureInvariant);

    private readonly string _databaseName =
        $"{Prefix}{Guid.NewGuid().ToString("N").ToUpperInvariant()}";

    private SqlConnection? _masterConnection;
    private bool _disposed;

    public string ConnectionString
        => new SqlConnectionStringBuilder
        {
            DataSource = DataSource,
            InitialCatalog = _databaseName,
            IntegratedSecurity = true,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = ConnectionTimeoutSeconds,
            MultipleActiveResultSets = true,
            Pooling = true
        }.ConnectionString;

    public AppDbContext CreateHostContext(
        IInterceptor? interceptor = null)
        => CreateContextCore(null, interceptor);

    public AppDbContext CreateTenantContext(
        int storeId,
        IInterceptor? interceptor = null)
    {
        if (storeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(storeId));
        }

        return CreateContextCore(storeId, interceptor);
    }

    public async Task MigrateAsync(
        string? targetMigration = null,
        CancellationToken ct = default)
    {
        await EnsureMasterConnectionAsync(ct);
        await using var db = CreateHostContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration, ct);
    }

    public async Task ExecuteAsync(
        string sql,
        CancellationToken ct = default)
    {
        await using var connection = LocalDbSqlConnectionFactory.Create(
            ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync(ct);
    }

    // Copy the exact security graph from the explicitly selected local test
    // dataset. Source commands are SELECT-only; all writes target our GUID DB.
    internal async Task<IReadOnlyDictionary<string, long>> CopySecurityStandardDataAsync(CancellationToken ct = default)
    {
        GuardDatabaseName();
        var sourceName = Environment.GetEnvironmentVariable("GAOAPP_SECURITY_STANDARD_SOURCE");
        if (sourceName != "GaoAppDb")
            throw new InvalidOperationException("Explicitly select the local GaoAppDb test dataset before this acceptance test.");
        await MigrateAsync(ct: ct);
        var sourceSettings = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = sourceName, ApplicationIntent = ApplicationIntent.ReadOnly };
        await using var source = new SqlConnection(sourceSettings.ConnectionString);
        await using var destination = new SqlConnection(ConnectionString);
        await source.OpenAsync(ct); await destination.OpenAsync(ct);
        await using var sourceTransaction = (SqlTransaction)await source.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var destinationTransaction = (SqlTransaction)await destination.BeginTransactionAsync(ct);
        string[] tables = ["Users", "Stores", "Permissions", "Roles", "RolePermissions", "UserInStores", "AdminMenuItems"];
        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            await using var empty = new SqlCommand($"SELECT COUNT_BIG(*) FROM dbo.[{table}]", destination, destinationTransaction);
            if ((long)(await empty.ExecuteScalarAsync(ct))! != 0)
                throw new InvalidOperationException("A standard-data import requires empty destination tables.");
            await using var columnsCommand = new SqlCommand("SELECT name FROM sys.columns WHERE object_id=OBJECT_ID(@table) AND is_computed=0 AND system_type_id<>189 ORDER BY column_id", source, sourceTransaction);
            columnsCommand.Parameters.AddWithValue("@table", "dbo." + table);
            var columns = new List<string>();
            await using (var columnsReader = await columnsCommand.ExecuteReaderAsync(ct))
                while (await columnsReader.ReadAsync(ct)) columns.Add(columnsReader.GetString(0));
            if (columns.Count == 0) throw new InvalidOperationException("The source security schema is incomplete.");
            var projection = string.Join(",", columns.Select(x => "[" + x.Replace("]", "]]") + "]"));
            await using var read = new SqlCommand($"SELECT {projection} FROM dbo.[{table}] WITH (TABLOCK,HOLDLOCK) ORDER BY Id", source, sourceTransaction);
            await using (var reader = await read.ExecuteReaderAsync(ct))
            {
                using var bulk = new SqlBulkCopy(destination, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.CheckConstraints, destinationTransaction)
                    { DestinationTableName = "dbo.[" + table + "]" };
                foreach (var column in columns) bulk.ColumnMappings.Add(column, column);
                await bulk.WriteToServerAsync(reader, ct);
            }
            var digestSql = $"SELECT COUNT_BIG(*),HASHBYTES('SHA2_256',(SELECT {projection} FROM dbo.[{table}] ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES)) FROM dbo.[{table}]";
            await using var originalDigest = new SqlCommand(digestSql, source, sourceTransaction);
            long count; byte[] hash;
            await using (var reader = await originalDigest.ExecuteReaderAsync(ct))
            {
                Assert.True(await reader.ReadAsync(ct)); count = reader.GetInt64(0); hash = (byte[])reader[1];
            }
            await using var copiedDigest = new SqlCommand(digestSql, destination, destinationTransaction);
            await using (var reader = await copiedDigest.ExecuteReaderAsync(ct))
            {
                Assert.True(await reader.ReadAsync(ct)); Assert.Equal(count, reader.GetInt64(0)); Assert.Equal(hash, (byte[])reader[1]);
            }
            counts.Add(table, count);
        }
        await destinationTransaction.CommitAsync(ct);
        await sourceTransaction.RollbackAsync(ct);
        return counts;
    }

    public async Task<InventoryPostingSeed> SeedInventoryCatalogAsync(
        bool allowNegativeInventory = false,
        CancellationToken ct = default)
    {
        await using var host = CreateHostContext();
        var subdomain = $"r2-{Guid.NewGuid():N}";
        var store = new Store
        {
            Name = "R2 Inventory Posting Store",
            SubDomain = subdomain,
            SubDomainNormalized = subdomain.ToUpperInvariant(),
            IsActive = true
        };
        await LegacyStoreSeed.InsertAsync(host, store, ct);

        await using var tenant = CreateTenantContext(store.Id);
        var legalEntity = new LegalEntity
        {
            StoreId = store.Id,
            Code = "R2-LEGAL",
            Name = "R2 Legal Entity",
            LegalName = "R2 Legal Entity Limited",
            IsActive = true
        };
        tenant.LegalEntities.Add(legalEntity);
        await tenant.SaveChangesAsync(ct);

        var warehouse = new Warehouse
        {
            StoreId = store.Id,
            LegalEntityId = legalEntity.Id,
            Code = "R2-WH",
            Name = "R2 Inventory Warehouse",
            IsActive = true,
            IsDefault = true,
            AllowNegativeInventory = allowNegativeInventory
        };
        var category = new Category
        {
            StoreId = store.Id,
            Code = "R2-CAT",
            Name = "R2 Category",
            IsActive = true
        };
        var unit = new Unit
        {
            StoreId = store.Id,
            Code = "R2-UNIT",
            Name = "R2 Unit",
            IsActive = true,
            IsBase = true
        };

        tenant.AddRange(warehouse, category, unit);
        await tenant.SaveChangesAsync(ct);

        // Seed baseline Supplier columns because schema-prefix tests intentionally
        // run before newer optional fields (such as bank fields) exist.
        await tenant.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [Suppliers] ([StoreId], [Code], [Name], [IsActive], [SortOrder], [CreatedAtUtc], [IsDeleted])
            VALUES ({store.Id}, N'R2-SUP', N'R2 Supplier', 1, 0, SYSUTCDATETIME(), 0);
            """, ct);
        var supplierId = await tenant.Database.SqlQuery<int>($"""
            SELECT [Id] AS [Value] FROM [Suppliers] WHERE [StoreId] = {store.Id} AND [Code] = N'R2-SUP'
            """).SingleAsync(ct);

        var product = new Product
        {
            StoreId = store.Id,
            Name = "R2 Product",
            Alias = "r2-product",
            CategoryId = category.Id,
            SupplierId = supplierId,
            BaseUnitId = unit.Id,
            BasePrice = 20m,
            IsActive = true,
            IsSellable = true
        };
        tenant.Products.Add(product);
        await tenant.SaveChangesAsync(ct);

        var variant = new ProductVariant
        {
            StoreId = store.Id,
            ProductId = product.Id,
            Sku = $"R2-{Guid.NewGuid():N}",
            CostPrice = 10m,
            Price = 20m,
            IsActive = true
        };
        tenant.ProductVariants.Add(variant);
        await tenant.SaveChangesAsync(ct);

        return new InventoryPostingSeed(
            store.Id,
            warehouse.Id,
            variant.Id);
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        CancellationToken ct = default)
    {
        await using var connection = LocalDbSqlConnectionFactory.Create(
            ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull
            ? default
            : (T)Convert.ChangeType(result, typeof(T));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GuardDatabaseName();
        ClearTargetConnectionPool();

        var connection = await EnsureMasterConnectionAsync(
            CancellationToken.None);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText =
                $"""
                 IF DB_ID(@databaseName) IS NOT NULL
                 BEGIN
                     ALTER DATABASE [{_databaseName}]
                         SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                     DROP DATABASE [{_databaseName}];
                 END
                 """;
            command.Parameters.Add(
                new SqlParameter(
                    "@databaseName",
                    SqlDbType.NVarChar,
                    128)
                {
                    Value = _databaseName
                });
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            ClearTargetConnectionPool();
            await connection.DisposeAsync();
            _masterConnection = null;
        }
    }

    private AppDbContext CreateContextCore(
        int? storeId,
        IInterceptor? interceptor)
    {
        var connection = LocalDbSqlConnectionFactory.Create(
            ConnectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: true);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        var tenant = new TenantContext();
        if (storeId.HasValue)
        {
            tenant.SetStore(storeId.Value, "r2-inventory-posting");
        }
        else
        {
            tenant.SetHostAdminForTest();
        }

        return new AppDbContext(
            options.Options,
            tenant,
            new InventoryPostingCurrentUser());
    }

    private async Task<SqlConnection> EnsureMasterConnectionAsync(
        CancellationToken ct)
    {
        if (_masterConnection is { State: ConnectionState.Open })
        {
            return _masterConnection;
        }

        if (_masterConnection is not null)
        {
            await _masterConnection.DisposeAsync();
        }

        _masterConnection = LocalDbSqlConnectionFactory.Create(
            new SqlConnectionStringBuilder
            {
                DataSource = DataSource,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                Encrypt = false,
                TrustServerCertificate = true,
                ConnectTimeout = ConnectionTimeoutSeconds,
                Pooling = true
            }.ConnectionString);
        await _masterConnection.OpenAsync(ct);
        return _masterConnection;
    }

    private void ClearTargetConnectionPool()
    {
        using var connection = new SqlConnection(ConnectionString);
        SqlConnection.ClearPool(connection);
    }

    private void GuardDatabaseName()
    {
        if (!SafeDatabaseName.IsMatch(_databaseName))
        {
            throw new InvalidOperationException(
                "Unsafe inventory posting LocalDB database name.");
        }
    }

    private sealed class InventoryPostingCurrentUser : ICurrentUser
    {
        public int? UserId => null;
        public string? UserName => null;
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => false;
    }
}

internal sealed record InventoryPostingSeed(
    int StoreId,
    int WarehouseId,
    int ProductVariantId);
