using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Data.Seed;

public enum ProductionBootstrapResult
{
    Disabled = 0,
    Created = 1,
    AlreadyProvisioned = 2
}

public enum ProductionBootstrapPlanState
{
    Disabled = 0,
    Empty = 1,
    Matching = 2,
    PartialOrDifferent = 3
}

public sealed record ProductionBootstrapPlan(
    ProductionBootstrapPlanState State,
    bool RequiresChanges);

public sealed class ProductionBootstrapStateException
    : InvalidOperationException
{
    public ProductionBootstrapStateException(string safeReasonCode)
        : base(
            $"Production bootstrap was rejected. ReasonCode={safeReasonCode}. No provisioning changes were applied.")
    {
        SafeReasonCode = safeReasonCode;
    }

    public string SafeReasonCode { get; }
}

/// <summary>
/// Kiểm tra và khởi tạo dữ liệu tối thiểu để database production trống có thể
/// đăng nhập và cấu hình tiếp. Không dùng dữ liệu hoặc mật khẩu mặc định.
/// Transaction của phase ghi dữ liệu do migration pipeline sở hữu.
/// </summary>
public sealed class ProductionBootstrapper : IProductionBootstrapper
{
    private readonly AppDbContext _db;
    private readonly ProductionBootstrapOptions _options;

    public ProductionBootstrapper(
        AppDbContext db,
        IOptions<ProductionBootstrapOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<ProductionBootstrapPlan> InspectAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Disabled,
                RequiresChanges: false);
        }

        var stores = await _db.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(ct);
        var users = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(ct);

        if (stores == 0 && users == 0)
        {
            return new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Empty,
                RequiresChanges: true);
        }

        var subdomain = Required(_options.StoreSubdomain).ToLowerInvariant();
        var userName = Required(_options.AdminUserName);

        var store = await _db.Stores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.SubDomainNormalized == subdomain,
                ct);
        var user = await _db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.UserName == userName,
                ct);

        if (store is null || user is null)
        {
            return PartialPlan();
        }

        var legalEntity = await _db.LegalEntities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.StoreId == store.Id
                    && entity.Code
                    == Required(_options.LegalEntityCode).ToUpperInvariant(),
                ct);
        var warehouse = await _db.Warehouses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.StoreId == store.Id
                    && entity.Code
                    == Required(_options.WarehouseCode).ToUpperInvariant(),
                ct);
        var terminal = await _db.POSTerminals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.StoreId == store.Id
                    && entity.Code
                    == Required(_options.TerminalCode).ToUpperInvariant(),
                ct);
        var hasAdminMapping = await _db.UserInStores
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                entity => entity.StoreId == store.Id
                    && entity.UserId == user.Id
                    && entity.IsActive
                    && !entity.Role.IsDeleted
                    && entity.Role.Code == "ADMIN",
                ct);

        var isMatching = store.IsActive
            && user.IsActive
            && user.IsHostAdmin
            && legalEntity is
            {
                IsActive: true,
                IsDefaultForPurchase: true
            }
            && warehouse is
            {
                IsActive: true,
                IsDefault: true
            }
            && warehouse.LegalEntityId == legalEntity.Id
            && legalEntity.DefaultWarehouseId == warehouse.Id
            && terminal is { IsActive: true }
            && hasAdminMapping;

        return isMatching
            ? new ProductionBootstrapPlan(
                ProductionBootstrapPlanState.Matching,
                RequiresChanges: false)
            : PartialPlan();
    }

    public async Task<ProductionBootstrapResult> ApplyAsync(
        ProductionBootstrapPlan plan,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        switch (plan.State)
        {
            case ProductionBootstrapPlanState.Disabled:
                return ProductionBootstrapResult.Disabled;

            case ProductionBootstrapPlanState.Matching:
                return ProductionBootstrapResult.AlreadyProvisioned;

            case ProductionBootstrapPlanState.PartialOrDifferent:
                throw new ProductionBootstrapStateException(
                    "PartialOrDifferent");

            case ProductionBootstrapPlanState.Empty:
                break;

            default:
                throw new ProductionBootstrapStateException(
                    "UnknownBootstrapPlan");
        }

        if (!_options.Enabled || !plan.RequiresChanges)
        {
            throw new ProductionBootstrapStateException(
                "InvalidBootstrapPlan");
        }

        if (_db.Database.IsRelational()
            && _db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Production bootstrap apply requires an active pipeline-owned transaction.");
        }

        var now = DateTime.UtcNow;
        var store = new Store
        {
            Name = Required(_options.StoreName),
            SubDomain = Required(_options.StoreSubdomain).ToLowerInvariant(),
            SubDomainNormalized =
                Required(_options.StoreSubdomain).ToLowerInvariant(),
            IsActive = true,
            IsMultiLegalEntityEnabled = false,
            CreatedAtUtc = now
        };

        _db.Stores.Add(store);
        await _db.SaveChangesAsync(ct);

        var legalEntity = new LegalEntity
        {
            StoreId = store.Id,
            Code = Required(_options.LegalEntityCode).ToUpperInvariant(),
            Name = Required(_options.LegalEntityName),
            LegalName = Required(_options.LegalEntityLegalName),
            TaxCode = Optional(_options.LegalEntityTaxCode),
            SalePriority = 1,
            IsDefaultForPurchase = true,
            IsActive = true,
            CreatedAtUtc = now
        };

        _db.LegalEntities.Add(legalEntity);
        await _db.SaveChangesAsync(ct);

        var warehouse = new Warehouse
        {
            StoreId = store.Id,
            LegalEntityId = legalEntity.Id,
            Code = Required(_options.WarehouseCode).ToUpperInvariant(),
            Name = Required(_options.WarehouseName),
            IsDefault = true,
            IsActive = true,
            AllowNegativeInventory = false,
            CreatedAtUtc = now
        };
        var terminal = new POSTerminal
        {
            StoreId = store.Id,
            Code = Required(_options.TerminalCode).ToUpperInvariant(),
            Name = Required(_options.TerminalName),
            IsActive = true,
            CreatedAtUtc = now
        };
        var admin = new User
        {
            UserName = Required(_options.AdminUserName),
            FullName = Required(_options.AdminFullName),
            Email = Optional(_options.AdminEmail)?.ToLowerInvariant(),
            PasswordHash = PasswordHasherHelper.Hash(
                RequiredSecret(_options.AdminPassword)),
            IsActive = true,
            IsHostAdmin = true,
            CreatedAtUtc = now
        };

        _db.Warehouses.Add(warehouse);
        _db.POSTerminals.Add(terminal);
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);

        legalEntity.DefaultWarehouseId = warehouse.Id;
        await _db.SaveChangesAsync(ct);

        // Mandatory seed ran before the Store existed. Seed the Store-scoped
        // mandatory fixtures now, inside the same outer transaction, so the
        // first successful bootstrap is complete and the next run is a no-op.
        await SecuritySeedData.SeedLegalEntityAdminMenusAsync(_db, ct);
        await SecuritySeedData.SeedPurchaseAdminMenusAsync(_db, ct);
        await SecuritySeedData.SeedDefaultRolesForStoreAsync(
            _db,
            store.Id,
            ct);
        await SecuritySeedData.SeedUserInStoreAsync(
            _db,
            store.Id,
            admin.Id,
            "ADMIN",
            ct: ct);

        return ProductionBootstrapResult.Created;
    }

    private static ProductionBootstrapPlan PartialPlan()
        => new(
            ProductionBootstrapPlanState.PartialOrDifferent,
            RequiresChanges: false);

    private static string Required(string? value)
        => value?.Trim()
            ?? throw new InvalidOperationException(
                "Production bootstrap configuration is invalid.");

    private static string? Optional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequiredSecret(string? value)
        => value
            ?? throw new InvalidOperationException(
                "Production bootstrap configuration is invalid.");
}
