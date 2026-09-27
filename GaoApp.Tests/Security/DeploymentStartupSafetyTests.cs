using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class DeploymentStartupSafetyTests
{
    [Fact]
    public async Task Explicit_fixture_initialization_prepares_both_stores_without_Web_seeding()
    {
        await using var fixture = await FullApplicationFixture.StartAsync();
        await using var db = fixture.Database.CreateHostContext();
        Assert.Equal(2, fixture.Stores.Count);
        foreach (var store in fixture.Stores)
        {
            Assert.Equal(4, await db.Roles.IgnoreQueryFilters().CountAsync(x => x.StoreId == store.StoreId));
            Assert.True(await db.AdminMenuItems.IgnoreQueryFilters().AnyAsync(x => x.StoreId == store.StoreId));
        }
        Assert.True(await db.Permissions.AnyAsync());
        Assert.Equal(0, await db.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Published_migrator_rejects_bad_arguments_before_reading_configuration_or_sql()
    {
        var release = await PublishedTestRelease.GetAsync();
        foreach (var arguments in new[] { Array.Empty<string>(), new[] { "--bad-mode" }, new[] { "--schema-only", "--bootstrap" }, new[] { "--schema-only", "--schema-only" } })
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(release, "migrator", "GaoApp.Migrator.dll"));
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            // Would fail DI if the command parser allowed startup to proceed.
            start.Environment["ConnectionStrings__DefaultConnection"] = "INVALID-SYNTHETIC-CONNECTION";
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(2, process.ExitCode);
            Assert.Empty(await stdout);
            Assert.Contains("Usage:", await stderr);
            Assert.DoesNotContain("INVALID-SYNTHETIC-CONNECTION", await stderr);
        }
    }

    [Fact]
    public async Task Real_development_web_starts_without_creating_any_security_or_demo_rows()
    {
        await using var fixture = await FullApplicationFixture.StartWithSecurityAsync(initializeSecurity: false);
        await using var db = fixture.Database.CreateTenantContext(fixture.Stores[0].StoreId);
        Assert.Equal(0, await db.Permissions.CountAsync());
        Assert.Equal(0, await db.Roles.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.AdminMenuItems.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await db.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.Stores.IgnoreQueryFilters().CountAsync());
        Assert.Equal(43, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }
}
