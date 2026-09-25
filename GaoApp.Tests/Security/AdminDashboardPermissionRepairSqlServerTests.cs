using System.Net;
using GaoApp.Application.Common.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class AdminDashboardPermissionRepairSqlServerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dashboard_repair_restores_default_roles_without_granting_other_roles_or_stores(bool missingPermission)
    {
        Assert.Single(PermissionCatalog.All, x => x.Code == PermissionCodes.Admin.DashboardView);
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var restrictedAccount = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        string subdomain;
        await using (var db = app.Database.CreateHostContext())
        {
            subdomain = (await db.Stores.SingleAsync(x => x.Id == store.StoreId)).SubDomain;
            var seededRoles = await db.RolePermissions
                .Where(x => x.Role.StoreId == store.StoreId && x.Permission.Code == PermissionCodes.Admin.DashboardView)
                .Select(x => x.Role.Code).OrderBy(x => x).ToArrayAsync();
            Assert.Equal(new[] { "ADMIN", "CASHIER", "MANAGER" }, seededRoles);

            var admin = await db.Roles.SingleAsync(x => x.StoreId == store.StoreId && x.Code == "ADMIN");
            (await db.UserInStores.SingleAsync(x => x.UserId == account.UserId && x.StoreId == store.StoreId)).RoleId = admin.Id;
            var grants = db.RolePermissions.Where(x => x.Permission.Code == PermissionCodes.Admin.DashboardView);
            if (!missingPermission)
                grants = grants.Where(x => x.Role.StoreId == store.StoreId);
            db.RolePermissions.RemoveRange(await grants.ToListAsync());
            await db.SaveChangesAsync();
            if (missingPermission)
            {
                db.Permissions.Remove(await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Admin.DashboardView));
                await db.SaveChangesAsync();
            }
        }

        using var client = await app.LoginAsync(account);
        client.Http.DefaultRequestHeaders.Accept.Clear();
        using (var denied = await client.Http.GetAsync("/admin"))
        {
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("/admin/account/access-denied?ReturnUrl=%2Fadmin", denied.Headers.Location!.ToString());
        }
        var deniedHtml = WebUtility.HtmlDecode(await client.Http.GetStringAsync("/admin/account/access-denied?ReturnUrl=%2Fadmin"));
        Assert.Contains("Trang tổng quan quản trị (GaoApp Dashboard)", deniedHtml);

        var source = await File.ReadAllTextAsync(Path.Combine(FullApplicationFixture.SourceRoot(), "docs", "admin-dashboard-permission-repair.sql"));
        var preview = source.Replace("N'chonthanh'", "N'" + subdomain.Replace("'", "''") + "'");
        var apply = preview.Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;");
        await using var check = app.Database.CreateHostContext();
        var beforeGrants = await check.RolePermissions.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var beforePermissions = await check.Permissions.CountAsync();
        await check.Database.ExecuteSqlRawAsync(preview);
        Assert.Equal(beforePermissions, await check.Permissions.CountAsync());
        Assert.Equal(beforeGrants, await check.RolePermissions.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());

        var wrongStore = source.Replace("N'chonthanh'", "N'nonexistent-store'").Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = 1;");
        var rejected = await Assert.ThrowsAsync<SqlException>(() => check.Database.ExecuteSqlRawAsync(wrongStore));
        Assert.Equal(51071, rejected.Number);
        Assert.Equal(beforePermissions, await check.Permissions.CountAsync());
        Assert.Equal(beforeGrants, await check.RolePermissions.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());

        await check.Database.ExecuteSqlRawAsync(apply);
        await check.Database.ExecuteSqlRawAsync(apply);
        Assert.Equal(beforePermissions + (missingPermission ? 1 : 0), await check.Permissions.CountAsync());
        Assert.Equal(beforeGrants.Length + 3, await check.RolePermissions.CountAsync());
        var added = await check.RolePermissions.Where(x => !beforeGrants.Contains(x.Id))
            .Select(x => new { x.Role.StoreId, x.Role.Code, x.Role.IsSystemRole, Permission = x.Permission.Code }).ToArrayAsync();
        Assert.All(added, x =>
        {
            Assert.Equal(store.StoreId, x.StoreId);
            Assert.True(x.IsSystemRole);
            Assert.Equal(PermissionCodes.Admin.DashboardView, x.Permission);
        });
        Assert.Equal(new[] { "ADMIN", "CASHIER", "MANAGER" }, added.Select(x => x.Code).OrderBy(x => x).ToArray());
        Assert.Equal(beforeGrants, await check.RolePermissions.Where(x => beforeGrants.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());

        using var allowed = await client.Http.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("GaoApp Dashboard", await allowed.Content.ReadAsStringAsync());
        using var restricted = await app.LoginAsync(restrictedAccount);
        using var stillDenied = await restricted.Http.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Forbidden, stillDenied.StatusCode);
    }
}
