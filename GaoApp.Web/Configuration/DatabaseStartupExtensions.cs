using GaoApp.Application.Common.Options;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Extension phục vụ Development startup:
/// - auto migrate database
/// - seed permission an toàn
/// - seed user/role demo cho môi trường dev
///
/// Lưu ý:
/// - Chỉ nên gọi từ GaoApp.Web trong môi trường Development
/// - Production/Staging nên dùng GaoApp.Migrator riêng
/// </summary>
public static class DatabaseStartupExtensions
{
    public static async Task MigrateAndSeedDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;

        var db = services.GetRequiredService<AppDbContext>();
        var seedOptions = services
            .GetRequiredService<IOptions<SeedDataOptions>>()
            .Value;

            // ============================================
            // MIGRATION
            // ============================================
            await db.Database.MigrateAsync();

            // ============================================
            // LUÔN CHẠY (production-safe)
            // Permission seed là an toàn cho mọi môi trường
            // ============================================
            await SecuritySeedData.SeedPermissionsAsync(db);
            await SecuritySeedData.SeedLegalEntityAdminMenusAsync(db);
            await SecuritySeedData.SeedPurchaseAdminMenusAsync(db);

            // ============================================
            // CHỈ DEV
            // ============================================
            await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(db);

            if (app.Environment.IsDevelopment() && seedOptions.EnableDemoSeed)
            {
                await SecuritySeedData.SeedUsersAsync(
                    db,
                    seedOptions.DemoUserPassword ?? string.Empty);

                var storeIds = await db.Stores
                    .AsNoTracking()
                    .Select(x => x.Id)
                    .ToListAsync();

                foreach (var storeId in storeIds)
                {
                    await SecuritySeedData.SeedUserInStoresAsync(db, storeId);
                }
            }
    }
}
