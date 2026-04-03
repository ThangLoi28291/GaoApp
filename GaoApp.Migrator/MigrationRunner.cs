using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Migrator;

/// <summary>
/// Chạy migration + seed dữ liệu nền cho GaoApp.
/// </summary>
public class MigrationRunner
{
    private readonly AppDbContext _db;

    public MigrationRunner(AppDbContext db)
    {
        _db = db;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("GaoApp Migrator started");
        Console.WriteLine("=================================================");
        Console.WriteLine();

        Console.WriteLine($"Database Provider : {_db.Database.ProviderName}");
        Console.WriteLine($"Database Name     : {_db.Database.GetDbConnection().Database}");
        Console.WriteLine();

        // ------------------------------------------------------
        // 1. Apply migration
        // ------------------------------------------------------
        Console.WriteLine("Applying migrations...");
        await _db.Database.MigrateAsync(ct);
        Console.WriteLine("Migrations applied successfully.");
        Console.WriteLine();

        // ------------------------------------------------------
        // 2. Seed dữ liệu nền
        // ------------------------------------------------------
        Console.WriteLine("Seeding security data...");

        // 2.1 Seed permission global
        await SecuritySeedData.SeedPermissionsAsync(_db, ct);

        // 2.2 Seed user test
        await SecuritySeedData.SeedUsersAsync(_db, ct);

        // 2.3 Seed role mặc định cho toàn bộ store
        await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(_db, ct);

        // 2.4 Seed mapping user-store cho từng store đang có
        var storeIds = await _db.Stores
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var storeId in storeIds)
        {
            await SecuritySeedData.SeedUserInStoresAsync(_db, storeId, ct);
        }

        Console.WriteLine("Security seed completed.");
        Console.WriteLine();

        Console.WriteLine("=================================================");
        Console.WriteLine("GaoApp Migrator completed successfully");
        Console.WriteLine("=================================================");
    }
}