using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Development-only schema bootstrap for the local Visual Studio profile.
/// Production and staging continue to use the explicit Migrator process.
/// </summary>
public static class DevelopmentDatabaseMigrationExtensions
{
    public static async Task ApplyDevelopmentSchemaAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        if (!app.Environment.IsDevelopment() ||
            !app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();

        Log.Information(
            "Development migration check started for {Database} on {DataSource}",
            connection.Database,
            connection.DataSource);

        await db.Database.MigrateAsync(cancellationToken);

        Log.Information("Development migration check completed successfully");
    }
}
