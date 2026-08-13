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
    private const string DataSource = @"(localdb)\MSSQLLocalDB";
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
        host.Stores.Add(store);
        await host.SaveChangesAsync(ct);

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
        var supplier = new Supplier
        {
            StoreId = store.Id,
            Code = "R2-SUP",
            Name = "R2 Supplier",
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

        tenant.AddRange(warehouse, category, supplier, unit);
        await tenant.SaveChangesAsync(ct);

        var product = new Product
        {
            StoreId = store.Id,
            Name = "R2 Product",
            Alias = "r2-product",
            CategoryId = category.Id,
            SupplierId = supplier.Id,
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
            tenant.SetHostAdmin();
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
