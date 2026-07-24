using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Migrator;

/// <summary>
/// Chạy migration + seed dữ liệu nền cho GaoApp.
/// </summary>
public class MigrationRunner
{
    private readonly AppDbContext _db;
    private readonly SeedDataOptions _seedOptions;

    public MigrationRunner(
        AppDbContext db,
        IOptions<SeedDataOptions> seedOptions)
    {
        _db = db;
        _seedOptions = seedOptions.Value;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        ValidateSeedOptions();

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
        await SecuritySeedData.SeedLegalEntityAdminMenusAsync(_db, ct);
        await SecuritySeedData.SeedPurchaseAdminMenusAsync(_db, ct);

        // 2.2 Role hệ thống không chứa secret và an toàn để đồng bộ.
        await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(_db, ct);

        // Tài khoản/mapping demo chỉ được tạo khi bật rõ ràng.
        // Mặc định false để Migrator production không tạo tài khoản biết trước mật khẩu.
        if (_seedOptions.EnableDemoSeed)
        {
            Console.WriteLine("Demo seed is explicitly enabled.");
            await SecuritySeedData.SeedUsersAsync(
                _db,
                _seedOptions.DemoUserPassword ?? string.Empty,
                ct);

            var storeIds = await _db.Stores
                .AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync(ct);

            foreach (var storeId in storeIds)
            {
                await SecuritySeedData.SeedUserInStoresAsync(_db, storeId, ct);
            }
        }
        else
        {
            Console.WriteLine("Demo user seed skipped.");
        }

        Console.WriteLine("Security seed completed.");
        Console.WriteLine();

        Console.WriteLine("=================================================");
        Console.WriteLine("GaoApp Migrator completed successfully");
        Console.WriteLine("=================================================");
    }

    private void ValidateSeedOptions()
    {
        if (_seedOptions.EnableDemoSeed &&
            string.IsNullOrWhiteSpace(_seedOptions.DemoUserPassword))
        {
            throw new InvalidOperationException(
                "Startup validation failed: SeedData:DemoUserPassword bắt buộc khi SeedData:EnableDemoSeed = true.");
        }
    }
}
