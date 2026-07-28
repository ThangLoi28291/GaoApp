using GaoApp.Application.Common.Options;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Seed;

public interface IMandatorySecuritySeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

public interface IDemoDataSeeder
{
    Task SeedAsync(
        SeedDataOptions options,
        CancellationToken ct = default);
}

public interface IProductionBootstrapper
{
    Task<ProductionBootstrapPlan> InspectAsync(
        CancellationToken ct = default);

    Task<ProductionBootstrapResult> ApplyAsync(
        ProductionBootstrapPlan plan,
        CancellationToken ct = default);
}

public interface IProvisioningTransactionRunner
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default);
}

public sealed class EfCoreProvisioningTransactionRunner
    : IProvisioningTransactionRunner
{
    private readonly AppDbContext _db;

    public EfCoreProvisioningTransactionRunner(AppDbContext db)
    {
        _db = db;
    }

    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ct.ThrowIfCancellationRequested();

        if (!_db.Database.IsRelational())
        {
            await operation(ct);
            return;
        }

        if (_db.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "Atomic provisioning requires pipeline transaction ownership.");
        }

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                ct);

        try
        {
            await operation(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception operationException)
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    "Atomic provisioning failed and transaction rollback also failed.",
                    operationException,
                    rollbackException);
            }

            throw;
        }
    }
}

public sealed class MandatorySecuritySeeder : IMandatorySecuritySeeder
{
    private readonly AppDbContext _db;

    public MandatorySecuritySeeder(AppDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SecuritySeedData.SeedPermissionsAsync(_db, ct);
        await SecuritySeedData.SeedLegalEntityAdminMenusAsync(_db, ct);
        await SecuritySeedData.SeedPurchaseAdminMenusAsync(_db, ct);
        await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(_db, ct);
    }
}

public sealed class DemoDataSeeder : IDemoDataSeeder
{
    private readonly AppDbContext _db;

    public DemoDataSeeder(AppDbContext db)
    {
        _db = db;
    }

    public async Task SeedAsync(
        SeedDataOptions options,
        CancellationToken ct = default)
    {
        await SecuritySeedData.SeedUsersAsync(
            _db,
            options.DemoUserPassword ?? string.Empty,
            ct);

        var storeIds = await _db.Stores
            .AsNoTracking()
            .Select(store => store.Id)
            .ToListAsync(ct);

        foreach (var storeId in storeIds)
        {
            await SecuritySeedData.SeedUserInStoresAsync(
                _db,
                storeId,
                ct);
        }
    }
}
