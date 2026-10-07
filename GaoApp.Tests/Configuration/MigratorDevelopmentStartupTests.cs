using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Tests.Security;
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
        var root = FullApplicationFixture.SourceRoot();
        var release = await PublishedTestRelease.GetAsync();
        var manifestPath = Path.Combine(release, "release-manifest.json");
        Assert.True(File.Exists(manifestPath), "Verify the paired release before this entry-point regression.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var components = manifest.RootElement.GetProperty("components").EnumerateArray()
            .Select(x => x.GetString()).ToArray();
        Assert.Contains("Web", components);
        Assert.Contains("Migrator", components);
        var files = manifest.RootElement.GetProperty("files").EnumerateArray().ToArray();
        foreach (var assembly in new[] { "GaoApp.Migrator.dll", "GaoApp.Application.dll", "GaoApp.Domain.dll", "GaoApp.Infrastructure.dll" })
        {
            var assemblyPath = Path.Combine(release, "migrator", assembly);
            Assert.True(File.Exists(assemblyPath), $"Published Migrator assembly is missing: {assembly}");
            var recorded = Assert.Single(files, x => x.GetProperty("path").GetString() == "migrator/" + assembly);
            Assert.Equal(recorded.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(assemblyPath))));
        }
        var executable = Path.Combine(release, "migrator", "GaoApp.Migrator.dll");
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
            Assert.Contains("SCHEMA_ONLY_VERIFIED; SourceMigrations=71; AppliedMigrations=71", diagnostic);
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
