using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosHoldPermissionRepairSqlServerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Existing_database_repair_restores_hold_without_changing_other_roles_or_stores(bool missingPermission)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        string subdomain;
        await using (var db = app.Database.CreateHostContext())
        {
            subdomain = (await db.Stores.SingleAsync(x => x.Id == store.StoreId)).SubDomain;
            var admin = await db.Roles.SingleAsync(x => x.StoreId == store.StoreId && x.Code == "ADMIN");
            (await db.UserInStores.SingleAsync(x => x.UserId == account.UserId && x.StoreId == store.StoreId)).RoleId = admin.Id;
            var grants = db.RolePermissions.Where(x => x.Permission.Code == PermissionCodes.Pos.Order.Hold);
            if (!missingPermission)
                grants = grants.Where(x => x.Role.StoreId == store.StoreId && (x.Role.Code == "ADMIN" || x.Role.Code == "CASHIER"));
            db.RolePermissions.RemoveRange(await grants.ToListAsync());
            await db.SaveChangesAsync();
            if (missingPermission)
            {
                db.Permissions.Remove(await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Pos.Order.Hold));
                await db.SaveChangesAsync();
            }
        }
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/ensure", new { });
        var orderId = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=1", new { });
        using (var denied = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/hold", new { holdNote = "before repair" }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var source = await File.ReadAllTextAsync(Path.Combine(FullApplicationFixture.SourceRoot(), "docs", "pos-hold-permission-repair.sql"));
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
        Assert.Equal(51061, rejected.Number);
        Assert.Equal(beforeGrants, await check.RolePermissions.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());

        await check.Database.ExecuteSqlRawAsync(apply);
        await check.Database.ExecuteSqlRawAsync(apply); // Rerunning the deployment repair must be safe.
        Assert.Equal(beforePermissions + (missingPermission ? 1 : 0), await check.Permissions.CountAsync());
        Assert.Equal(beforeGrants.Length + 2, await check.RolePermissions.CountAsync());
        var added = await check.RolePermissions.Where(x => !beforeGrants.Contains(x.Id))
            .Select(x => new { x.Role.StoreId, x.Role.Code, Permission = x.Permission.Code }).ToArrayAsync();
        Assert.All(added, x => { Assert.Equal(store.StoreId, x.StoreId); Assert.Equal(PermissionCodes.Pos.Order.Hold, x.Permission); });
        Assert.Equal(new[] { "ADMIN", "CASHIER" }, added.Select(x => x.Code).OrderBy(x => x).ToArray());
        Assert.Equal(beforeGrants, await check.RolePermissions.Where(x => beforeGrants.Contains(x.Id)).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var held = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/hold", new { holdNote = "after repair" });
        Assert.Equal(orderId, held.GetProperty("heldOrderId").GetInt32());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/resume", new { });
        Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync(x => x.StoreId == store.StoreId)).OnHandQty);
    }
}
