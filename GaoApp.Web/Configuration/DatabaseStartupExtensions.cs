using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;
using Serilog;

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

        try
        {
            var db = services.GetRequiredService<AppDbContext>();

            // ============================================
            // MIGRATION
            // ============================================
            await db.Database.MigrateAsync();

            // ============================================
            // LUÔN CHẠY (production-safe)
            // Permission seed là an toàn cho mọi môi trường
            // ============================================
            await SecuritySeedData.SeedPermissionsAsync(db);

            // ============================================
            // CHỈ DEV
            // ============================================
            if (app.Environment.IsDevelopment())
            {
                await SecuritySeedData.SeedUsersAsync(db);
                await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(db);

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
        catch (Exception ex)
        {
            Log.Fatal(ex, "Database migration/seed failed in GaoApp.Web");
            throw;
        }
    }
}