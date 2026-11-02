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
        using var access = scope.ServiceProvider.GetRequiredService<GaoApp.Infrastructure.Tenant.TenantContext>()
            .BeginSystemAccess(GaoApp.Infrastructure.Tenant.SystemDataAccessPurpose.Provisioning);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = db.Database.GetDbConnection();

        Log.Information(
            "Development migration check started for {Database} on {DataSource}",
            connection.Database,
            connection.DataSource);

        // The repository has reviewed hand-authored delta migrations. Do not invoke
        // EF's model-drift validation for a no-op startup; a pending migration still
        // follows the normal strict MigrateAsync path below.
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length == 0)
        {
            Log.Information("Development database has no pending migrations");
            return;
        }

        var commandTimeoutSeconds = app.Configuration.GetValue(
            "Database:MigrationCommandTimeoutSeconds", 300);
        if (commandTimeoutSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException(
                "Database:MigrationCommandTimeoutSeconds must be between 1 and 3600.");
        }

        // Adding constraints to existing order data can exceed the normal
        // request timeout. Give only this migration context a longer budget.
        var previousCommandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(commandTimeoutSeconds);
        try
        {
            Log.Information(
                "Applying {MigrationCount} development migrations with a {CommandTimeoutSeconds}s command timeout",
                pending.Length,
                commandTimeoutSeconds);
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            db.Database.SetCommandTimeout(previousCommandTimeout);
        }

        Log.Information("Development migration check completed successfully");
    }
}
