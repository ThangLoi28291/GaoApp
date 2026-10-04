using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class MigratorDevelopmentStartupTests
{
    [Fact]
    public async Task Schema_only_entry_point_validates_development_service_graph_and_is_repeatable_without_seeding()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20261001121734_PreserveInputInvoiceLibrarySource");
        var root = GaoApp.Tests.Security.FullApplicationFixture.SourceRoot();
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var executable = Path.Combine(root, "GaoApp.Migrator", "bin", configuration, "net8.0", "GaoApp.Migrator.dll");
        Assert.True(File.Exists(executable), "Build the solution including Migrator before this entry-point regression.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(executable);
            start.ArgumentList.Add("--schema-only");
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ConnectionStrings__DefaultConnection"] = database.ConnectionString;
            start.Environment["SeedData__EnableDemoSeed"] = "false";
            start.Environment["ProductionBootstrap__EnableBootstrap"] = "false";
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            var diagnostic = await output + await error;
            Assert.True(process.ExitCode == 0, diagnostic);
            Assert.Contains("SCHEMA_ONLY_VERIFIED; SourceMigrations=60; AppliedMigrations=60", diagnostic);
        }
        await using var db = database.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(await db.Stores.AnyAsync());
        Assert.False(await db.Users.AnyAsync());
        Assert.False(await db.Permissions.AnyAsync());
        Assert.False(await db.StockDocuments.AnyAsync());
        Assert.False(await db.PurchaseReceiptPricingPlans.AnyAsync());
    }
}
