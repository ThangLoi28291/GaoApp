using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class TenantDataScopeSqlServerTests
{
    private static readonly Func<AppDbContext, int> CompiledCount = EF.CompileQuery((AppDbContext db) => db.Categories.Count());

    [Fact]
    public async Task Missing_context_and_admin_routing_cannot_execute_cached_ignored_compiled_navigation_or_raw_queries()
    {
        await using var fixture = new InventoryPostingLocalDb(); await fixture.MigrateAsync();
        var one = await fixture.SeedInventoryCatalogAsync(); var two = await fixture.SeedInventoryCatalogAsync();
        foreach (var mode in new[] { "async", "sync", "ignored", "ignored-sync", "compiled", "include", "string-include", "projection", "user-navigation", "sql", "sql-global", "raw-scalar", "raw-scalar-spoof" })
        {
            // Warm the shared EF query cache using a valid store first.
            await using (var warm = fixture.CreateTenantContext(one.StoreId)) await Query(warm, mode, one.StoreId);
            foreach (var adminRouting in new[] { false, true })
            {
                var tenant = new TenantContext(); if (adminRouting) tenant.SetHostAdmin();
                await using var missing = Context(fixture, tenant);
                await Assert.ThrowsAsync<TenantDataScopeException>(() => Query(missing, mode, one.StoreId));
            }
        }
        await using var metadata = Context(fixture, new TenantContext());
        Assert.Equal(2, await metadata.Stores.CountAsync()); // Needed before resolving a tenant.
        Assert.Equal(0, await metadata.Users.CountAsync()); // Needed for authentication.

        static async Task<int> Query(AppDbContext db, string mode, int store) => mode switch
        {
            "async" => await db.Categories.CountAsync(),
            "sync" => db.Categories.Count(),
            "ignored" => await db.Categories.IgnoreQueryFilters().Where(x => x.StoreId == store).CountAsync(),
            "ignored-sync" => db.Categories.IgnoreQueryFilters().Where(x => x.StoreId == store).Count(),
            "compiled" => CompiledCount(db),
            "include" => (await db.Stores.Include(x => x.LegalEntities).ToListAsync()).Count,
            "string-include" => (await db.Stores.Include("LegalEntities").IgnoreQueryFilters().ToListAsync()).Count,
            "projection" => (await db.Stores.Select(x => x.LegalEntities.Count).ToListAsync()).Count,
            "user-navigation" => (await db.Users.IgnoreQueryFilters().Select(x => x.UserInStores.Count).ToListAsync()).Count,
            "sql" => await db.Categories.FromSqlRaw("SELECT * FROM [Category]").CountAsync(),
            "sql-global" => await db.Stores.FromSqlRaw("SELECT * FROM [Stores]").CountAsync(),
            "raw-scalar" => await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Category]").SingleAsync(),
            "raw-scalar-spoof" => await db.Database.SqlQueryRaw<int>("-- GaoApp:global-metadata\nSELECT COUNT(*) AS [Value] FROM [Category]").SingleAsync(),
            _ => throw new ArgumentException(mode)
        };
    }

    [Fact]
    public async Task Writes_require_a_scope_and_store_ownership_is_checked_even_for_detached_entities()
    {
        await using var fixture = new InventoryPostingLocalDb(); await fixture.MigrateAsync();
        var one = await fixture.SeedInventoryCatalogAsync(); var two = await fixture.SeedInventoryCatalogAsync();
        int foreignId; byte[] foreignVersion;
        await using (var foreign = fixture.CreateTenantContext(two.StoreId))
        {
            var row = await foreign.Categories.SingleAsync(); foreignId = row.Id; foreignVersion = row.RowVersion;
        }
        var noTenant = new TenantContext();
        await using (var missing = Context(fixture, noTenant))
        {
            var row = new Category { StoreId = one.StoreId, Name = "must not be saved", Code = "UNSCOPED" };
            missing.Categories.Add(row);
            Assert.Throws<TenantDataScopeException>(() => missing.SaveChanges());
            await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.SaveChangesAsync(false));
            Assert.Equal(default, row.CreatedAtUtc);
            missing.ChangeTracker.Clear();
            missing.Attach(new Category { Id = foreignId, StoreId = one.StoreId, Name = "forged", Code = "FORGED", RowVersion = foreignVersion });
            missing.Entry(missing.ChangeTracker.Entries<Category>().Single().Entity).State = EntityState.Modified;
            await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.SaveChangesAsync());
            missing.ChangeTracker.Clear();
            await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Database.ExecuteSqlRawAsync("UPDATE [Category] SET [Name]='forged'"));
            await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Categories.ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "forged")));
            await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Categories.ExecuteDeleteAsync());
            using (noTenant.BeginSystemAccess(SystemDataAccessPurpose.BankCallbackDiscovery))
            {
                Assert.Equal(2, await missing.Categories.CountAsync());
                missing.Categories.Add(row);
                await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.SaveChangesAsync());
                missing.ChangeTracker.Clear();
                await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Database.ExecuteSqlRawAsync("UPDATE [Category] SET [Name]='forged'"));
                await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Categories.FromSqlRaw(
                    "UPDATE [Category] SET [Name]='forged' OUTPUT INSERTED.*").ToListAsync());
                await Assert.ThrowsAsync<TenantDataScopeException>(() => missing.Database.SqlQueryRaw<int>(
                    "UPDATE [Category] SET [Name]='forged'; SELECT 1 AS [Value]").ToListAsync());
            }
        }
        await using (var own = fixture.CreateTenantContext(one.StoreId))
        {
            Assert.Single(await own.Categories.ToListAsync());
            Assert.Null(await own.Categories.SingleOrDefaultAsync(x => x.Id == foreignId));
            own.Update(new Category { Id = foreignId, StoreId = one.StoreId, Name = "forged", Code = "FORGED", RowVersion = foreignVersion });
            await Assert.ThrowsAsync<InvalidOperationException>(() => own.SaveChangesAsync());
            own.ChangeTracker.Clear();
            own.Remove(new Category { Id = foreignId, StoreId = one.StoreId, RowVersion = foreignVersion });
            await Assert.ThrowsAsync<InvalidOperationException>(() => own.SaveChangesAsync());
        }
        await using var check = fixture.CreateTenantContext(two.StoreId);
        Assert.NotEqual("forged", (await check.Categories.SingleAsync()).Name);
    }

    [Fact]
    public async Task Only_a_fresh_valid_host_session_grants_cross_store_access_and_revocation_removes_it()
    {
        await using var fixture = new InventoryPostingLocalDb(); await fixture.MigrateAsync();
        await fixture.SeedInventoryCatalogAsync(); await fixture.SeedInventoryCatalogAsync();
        User host;
        await using (var setup = fixture.CreateHostContext())
        {
            host = new User { UserName = "explicit-host", PasswordHash = "synthetic-hash", IsActive = true, IsHostAdmin = true };
            setup.Users.Add(host); await setup.SaveChangesAsync();
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, host.Id.ToString()), new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(host)) }, "test"));
        var tenant = new TenantContext(); tenant.SetHostAdmin();
        await using var db = Context(fixture, tenant);
        Assert.False(tenant.CanReadAllStores);
        var validator = new SessionPrincipalValidator(db, tenant);
        Assert.True(await validator.ValidateAsync(principal)); Assert.True(tenant.CanReadAllStores);
        Assert.Equal(2, await db.Categories.CountAsync());
        await using (var revoke = fixture.CreateHostContext())
        {
            var user = await revoke.Users.SingleAsync(); user.IsActive = false; await revoke.SaveChangesAsync();
        }
        Assert.False(await validator.ValidateAsync(principal)); Assert.False(tenant.CanReadAllStores);
        await Assert.ThrowsAsync<TenantDataScopeException>(() => db.Categories.CountAsync());
    }

    private static AppDbContext Context(InventoryPostingLocalDb fixture, TenantContext tenant)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(fixture.ConnectionString).Options, tenant, new SystemUser());
    private sealed class SystemUser : ICurrentUser
    {
        public int? UserId => null; public string? UserName => null; public int? TerminalId => null;
        public string? TerminalCode => null; public bool IsAuthenticated => false;
    }
}
