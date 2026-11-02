using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PermissionCatalogDeltaSqlServerTests
{
    [Fact]
    public async Task Catalog_delta_previews_and_adds_missing_permissions_without_changing_role_grants()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();
        var migratedPermissionCount = await db.Permissions.CountAsync();
        var stores = Enumerable.Range(1, 2).Select(i => new Store
        {
            Name = $"Permission delta store {i}",
            SubDomain = $"permissions-{i}-{Guid.NewGuid():N}",
            IsActive = true
        }).ToArray();
        foreach (var store in stores)
        {
            store.SubDomainNormalized = store.SubDomain.ToUpperInvariant();
            await LegacyStoreSeed.InsertAsync(db, store);
        }

        var roles = new[]
        {
            new Role { StoreId = stores[0].Id, Code = "ADMIN", Name = "Admin", IsSystemRole = true },
            new Role { StoreId = stores[0].Id, Code = "CASHIER", Name = "Cashier", IsSystemRole = true },
            new Role { StoreId = stores[1].Id, Code = "ADMIN", Name = "Other store admin", IsSystemRole = true }
        };
        var existing = new Permission
        {
            Code = PermissionCodes.Admin.DashboardView,
            Name = "Tên quyền đã tùy chỉnh",
            GroupName = "Nhóm đã tùy chỉnh"
        };
        var custom = new Permission { Code = "custom.local.view", Name = "Custom", GroupName = "Custom" };
        db.AddRange(roles);
        db.AddRange(existing, custom);
        await db.SaveChangesAsync();
        var originalPermissions = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Code);
        var grants = roles.Select(r => new RolePermission { RoleId = r.Id, PermissionId = existing.Id }).ToArray();
        db.AddRange(grants);
        await db.SaveChangesAsync();
        var originalRoles = await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync();
        var originalGrants = await db.RolePermissions.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync();

        var script = await File.ReadAllTextAsync(Path.Combine(PermissionCatalogTestSource.Root(),
            "docs", "security-permissions-catalog-delta-20261009.sql"));
        await database.ExecuteAsync(script);
        Assert.Equal(migratedPermissionCount + 2, await db.Permissions.CountAsync());
        var apply = script.Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;");
        var wrongDatabase = apply.Replace("DECLARE @ExpectedDatabase sysname = N'';",
            "DECLARE @ExpectedDatabase sysname = N'not-the-selected-database';");
        var rejected = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(wrongDatabase));
        Assert.Equal(51302, rejected.Number);
        Assert.Equal(migratedPermissionCount + 2, await db.Permissions.CountAsync());

        var databaseName = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        apply = apply.Replace("DECLARE @ExpectedDatabase sysname = N'';",
            $"DECLARE @ExpectedDatabase sysname = N'{databaseName.Replace("'", "''")}';");
        // A server-side write failure must leave the catalog and grants untouched.
        await database.ExecuteAsync("""
            CREATE TRIGGER dbo.PermissionDeltaTestReject ON dbo.Permissions AFTER INSERT AS
            BEGIN
                THROW 51390, N'Owned fixture: simulated catalog write failure.', 1;
            END;
            """);
        var failed = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(apply));
        Assert.Equal(51390, failed.Number);
        Assert.Equal(migratedPermissionCount + 2, await db.Permissions.CountAsync());
        await database.ExecuteAsync("DROP TRIGGER dbo.PermissionDeltaTestReject;");

        await database.ExecuteAsync(apply);
        var afterFirst = await db.Permissions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync();
        await database.ExecuteAsync(apply);
        var afterSecond = await db.Permissions.AsNoTracking().OrderBy(p => p.Id).ToArrayAsync();
        Assert.Equal(PermissionCatalog.All.Count + 1, afterSecond.Length);
        Assert.Equal(afterFirst.Select(p => (p.Id, p.Code, p.Name, p.GroupName)),
            afterSecond.Select(p => (p.Id, p.Code, p.Name, p.GroupName)));
        foreach (var entry in PermissionCatalog.All)
        {
            var actual = Assert.Single(afterSecond, p => p.Code == entry.Code);
            var original = originalPermissions.GetValueOrDefault(entry.Code);
            Assert.Equal(original?.Name ?? entry.Name, actual.Name);
            Assert.Equal(original?.GroupName ?? entry.GroupName, actual.GroupName);
        }
        Assert.Equal(existing.Id, Assert.Single(afterSecond, p => p.Code == existing.Code).Id);
        Assert.Equal(custom.Id, Assert.Single(afterSecond, p => p.Code == custom.Code).Id);
        Assert.Equal(originalRoles.Select(r => (r.Id, r.StoreId, r.Code, r.Name, r.IsSystemRole)),
            (await db.Roles.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync())
                .Select(r => (r.Id, r.StoreId, r.Code, r.Name, r.IsSystemRole)));
        Assert.Equal(originalGrants.Select(r => (r.Id, r.RoleId, r.PermissionId)),
            (await db.RolePermissions.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync())
                .Select(r => (r.Id, r.RoleId, r.PermissionId)));
        Assert.Equal(0, await db.UserInStores.CountAsync());
        Assert.Equal(0, await db.AdminMenuItems.CountAsync());
    }
}
