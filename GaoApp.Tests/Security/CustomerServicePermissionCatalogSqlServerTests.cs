using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerServicePermissionCatalogSqlServerTests
{
    [Fact]
    public async Task Migration_and_repair_script_add_catalog_entries_without_assigning_roles_or_menus()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync("20261030140000_AddInvoiceRevenueTaxReduction");
        await using var db = database.CreateHostContext();
        var store = new Store { Name = "Permission fixture", SubDomain = "permissions-" + Guid.NewGuid().ToString("N"), IsActive = true };
        store.SubDomainNormalized = store.SubDomain.ToUpperInvariant();
        await LegacyStoreSeed.InsertAsync(db, store);
        var role = new Role { StoreId = store.Id, Code = "CSKH_TEST", Name = "Existing role" };
        var existing = new Permission { Code = "customer.service.view", Name = "Existing custom label", GroupName = "Custom" };
        var custom = new Permission { Code = "custom.local.view", Name = "Custom permission", GroupName = "Custom" };
        db.AddRange(role, existing, custom);
        await db.SaveChangesAsync();
        db.Add(new RolePermission { RoleId = role.Id, PermissionId = existing.Id });
        await db.SaveChangesAsync();
        var grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync();
        var roleCount = await db.Roles.CountAsync();
        var menuCount = await db.AdminMenuItems.CountAsync();

        await database.MigrateAsync("20261030150000_AddCustomerServicePermissionCatalog");
        string[] codes = ["customer.service.view", "customer.service.reply", "customer.service.manage", "customer.service.verify",
            PermissionCodes.CustomerService.InvoiceNotifications, PermissionCodes.CustomerService.MarketingConsent,
            AssistantPermissions.Use, AssistantPermissions.Chat, AssistantPermissions.Receiving, AssistantPermissions.NormalizeName,
            AssistantPermissions.Retail, AssistantPermissions.Wholesale];
        Assert.Equal(12, await db.Permissions.CountAsync(x => codes.Contains(x.Code)));
        Assert.Equal("Existing custom label", (await db.Permissions.SingleAsync(x => x.Code == existing.Code)).Name);

        // Simulate a partially installed catalog, preserving an existing role's grant.
        await db.Permissions.Where(x => x.Code == "customer.service.reply" || x.Code == AssistantPermissions.Chat).ExecuteDeleteAsync();
        var script = await File.ReadAllTextAsync(Path.Combine(PermissionCatalogTestSource.Root(), "docs", "deployment", "customer-service-permissions-20261030.sql"));
        await database.ExecuteAsync(script);
        Assert.Equal(10, await db.Permissions.CountAsync(x => codes.Contains(x.Code)));
        var apply = script.Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;");
        var rejected = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(apply));
        Assert.Equal(51302, rejected.Number);
        var name = new SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog.Replace("'", "''");
        apply = apply.Replace("DECLARE @ExpectedDatabase sysname = N'';", $"DECLARE @ExpectedDatabase sysname = N'{name}';");
        await database.ExecuteAsync(apply);
        var ids = await db.Permissions.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        await database.ExecuteAsync(apply);
        Assert.Equal(ids, await db.Permissions.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var actual = await db.Permissions.AsNoTracking().Where(x => codes.Contains(x.Code)).ToArrayAsync();
        Assert.Equal(12, actual.Length);
        foreach (var expected in PermissionCatalog.All.Where(x => codes.Contains(x.Code) && x.Code != existing.Code))
        {
            var row = Assert.Single(actual, x => x.Code == expected.Code);
            Assert.Equal(expected.Name, row.Name);
            Assert.Equal(expected.GroupName, row.GroupName);
        }
        Assert.True(await db.Permissions.AnyAsync(x => x.Id == custom.Id && x.Name == custom.Name));
        Assert.Equal(grants, await db.RolePermissions.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.RoleId, x.PermissionId }).ToArrayAsync());
        Assert.Equal(roleCount, await db.Roles.CountAsync());
        Assert.Equal(menuCount, await db.AdminMenuItems.CountAsync());
    }
}

internal static class PermissionCatalogTestSource
{
    internal static string Root([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "../.."));
}