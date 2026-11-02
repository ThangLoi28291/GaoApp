using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[CollectionDefinition("DeliveryD12", DisableParallelization = true)]
public sealed class DeliveryD12Collection : ICollectionFixture<DeliveryD02Fixture> { }

[Collection("DeliveryD12"), Trait("Category", "DeliveryD12")]
public sealed class DeliveryD12ReleaseTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Delivery_creation_is_available_without_activation_and_replays_without_duplicate_goods_or_money()
    {
        using var priced = await DeliveryPriceStockCase.PrepareAsync(fixture, "line");
        var original = await DeliveryD03Support.Request(priced.Source);
        var created = await priced.CreateAsync(original);
        await priced.Source.Client.JsonAsync(HttpMethod.Post, "/admin/pos/" + created.NextCartId +
            "/items?variantId=" + priced.FirstVariant + "&qty=1");
        var before = await priced.SnapshotAsync();
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await priced.CreateAsync(original)));
        Assert.Equal(before, await priced.SnapshotAsync());
        using (var read = await priced.Source.Client.Http.GetAsync(created.BillUrl)) Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        await priced.ReadyAsync();
        var courier = await fixture.Web.AddAccountAsync(priced.Source.Account.Store);
        var other = await fixture.Web.AddAccountAsync(priced.Source.Account.Store);
        using var trip = new DeliveryD06Case(fixture.Web, [new(priced.Inventory)], courier, other);
        await trip.CreateDraftAsync(); await trip.HandoverAsync(); await trip.DepartAsync();
        var till = await DeliveryD08Case.CounterAsync(fixture.Web, priced.Source.Account.Store);
        using var counter = new DeliveryD08Case(new(trip), till.Account, till.Client, till.ShiftId, till.CartId);
        var confirmation = await counter.RequestAsync(2);
        var ack = await counter.ConfirmAsync(confirmation);
        Assert.Equal("Settled", ack.State); Assert.Equal("16000", ack.Settlement.PayableText);
        var snapshot = await priced.Source.Client.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
        Assert.Contains(snapshot.GetProperty("delivery").GetProperty("orders").EnumerateArray(),
            x => x.GetProperty("orderId").GetInt32() == counter.Id && x.GetProperty("state").GetString() == "Settled");
        var after = await counter.SnapshotAsync();
        Assert.True((await counter.ConfirmAsync(confirmation)).Replayed);
        Assert.Equal(after, await counter.SnapshotAsync());
        var next = await till.Client.Http.GetFromJsonAsync<DeliveryCartSnapshot>("/admin/api/deliveries/current-cart");
        Assert.NotNull(next);
        using var response = await till.Client.Http.PostAsJsonAsync(DeliveryD03Support.CreatePath,
            original with { ClientRequestId = Guid.NewGuid(), SourceCartId = next.SourceCartId,
                ExpectedVersion = next.Version, ExpectedFingerprint = next.Fingerprint });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.NotEqual(counter.Id, (await response.Content.ReadFromJsonAsync<DeliveryPosResult>())!.Delivery.Id);
    }

    [Fact]
    public async Task Security_delta_preview_is_read_only_apply_is_insert_only_idempotent_and_preserves_other_stores()
    {
        var store = fixture.Web.Stores[0]; var foreign = fixture.Web.Stores[1];
        int roleId;
        await using (var db = fixture.Web.Database.CreateTenantContext(store.StoreId))
        {
            var role = new Role { StoreId = store.StoreId, Code = "D12-" + Guid.NewGuid().ToString("N")[..12], Name = "Custom retained role" };
            db.Roles.Add(role); await db.SaveChangesAsync(); roleId = role.Id;
            var view = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.View);
            view.Name = "Custom retained permission name";
            db.RolePermissions.Add(new() { RoleId = roleId, PermissionId = view.Id });
            var monitor = await db.AdminMenuItems.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.StoreId == store.StoreId && x.Url == "/admin/store-monitor");
            if (monitor is null)
            {
                monitor = new() { StoreId = store.StoreId, Url = "/admin/store-monitor", Area = "Admin",
                    Controller = "StoreMonitor", Action = "Index", Title = "Owned fixture monitor", IsSystem = true,
                    PermissionCode = PermissionCodes.Admin.StoreMonitorView };
                db.AdminMenuItems.Add(monitor);
                await db.SaveChangesAsync(); // Audit initializes new entities as not deleted; retire the saved row below.
            }
            monitor.Title = "Custom hidden monitor"; monitor.IsActive = false; monitor.IsDeleted = true;
            db.AdminMenuItems.Add(new() { StoreId = store.StoreId, Title = "Custom delivery entry", Area = "Admin",
                Controller = "DeliveryPage", Action = "Index", Url = "/admin/deliveries", IsActive = false, SortOrder = 1234 });
            await db.SaveChangesAsync();
        }
        // Remove one catalog entry solely inside the disposable test DB to exercise insert-missing.
        await using (var host = fixture.Web.Database.CreateHostContext())
        {
            var missing = await host.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.ReassignCourier);
            await host.RolePermissions.Where(x => x.PermissionId == missing.Id).ExecuteDeleteAsync();
            host.Permissions.Remove(missing); await host.SaveChangesAsync();
        }
        var grants = JsonSerializer.Serialize(new[] {
            new { roleId, permissionCode = PermissionCodes.Delivery.ReassignCourier },
            new { roleId, permissionCode = PermissionCodes.Delivery.View },
            new { roleId, permissionCode = PermissionCodes.Admin.StoreMonitorView } });
        var before = await SecuritySnapshotAsync();
        var foreignBefore = await SecuritySnapshotAsync(foreign.StoreId);
        await fixture.Web.Database.ExecuteAsync(Delta(store.StoreId, false, grants));
        Assert.Equal(before, await SecuritySnapshotAsync());
        await fixture.Web.Database.ExecuteAsync(Delta(store.StoreId, true, grants));
        Assert.Equal(foreignBefore, await SecuritySnapshotAsync(foreign.StoreId));
        await using (var check = fixture.Web.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal("Custom retained permission name", (await check.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.View)).Name);
            var menus = await check.AdminMenuItems.IgnoreQueryFilters().Where(x => x.StoreId == store.StoreId).ToArrayAsync();
            var delivery = Assert.Single(menus, x => x.Url == "/admin/deliveries");
            Assert.Equal("Custom delivery entry", delivery.Title); Assert.False(delivery.IsActive); Assert.Equal(1234, delivery.SortOrder);
            var monitor = Assert.Single(menus, x => x.Url == "/admin/store-monitor");
            Assert.Equal("Custom hidden monitor", monitor.Title); Assert.True(monitor.IsDeleted); Assert.False(monitor.IsActive);
            Assert.Single(menus, x => x.Url == "/admin/deliveries/trips");
            Assert.Equal(3, await check.RolePermissions.CountAsync(x => x.RoleId == roleId));
            var expected = PermissionCatalog.All.Where(x => x.Code.StartsWith("delivery.order.") || x.Code == PermissionCodes.Admin.StoreMonitorView)
                .Select(x => x.Code).Order().ToArray();
            Assert.Equal(expected, await check.Permissions.Where(x => expected.Contains(x.Code)).OrderBy(x => x.Code).Select(x => x.Code).ToArrayAsync());
        }
        var applied = await SecuritySnapshotAsync();
        await fixture.Web.Database.ExecuteAsync(Delta(store.StoreId, true, grants));
        Assert.Equal(applied, await SecuritySnapshotAsync());
    }

    [Fact]
    public async Task Security_delta_rejects_foreign_role_unknown_permission_or_wrong_database_before_any_writes()
    {
        var store = fixture.Web.Stores[0];
        var foreignRole = (await fixture.Web.AddAccountAsync(fixture.Web.Stores[1])).RoleId;
        var localRole = (await fixture.Web.AddAccountAsync(store)).RoleId;
        var before = await SecuritySnapshotAsync();
        foreach (var grants in new[] {
            JsonSerializer.Serialize(new[] { new { roleId = foreignRole, permissionCode = PermissionCodes.Delivery.View } }),
            JsonSerializer.Serialize(new[] { new { roleId = localRole, permissionCode = "security.role.permissions" } }), "{}" })
        {
            await Assert.ThrowsAsync<SqlException>(() => fixture.Web.Database.ExecuteAsync(Delta(store.StoreId, true, grants)));
            Assert.Equal(before, await SecuritySnapshotAsync());
        }
        var wrongDb = Delta(store.StoreId, true, "[]").Replace(new SqlConnectionStringBuilder(fixture.Web.Database.ConnectionString).InitialCatalog,
            "WrongD12Database");
        await Assert.ThrowsAsync<SqlException>(() => fixture.Web.Database.ExecuteAsync(wrongDb));
        Assert.Equal(before, await SecuritySnapshotAsync());
        var validEmpty = Delta(store.StoreId, true, "[]");
        var expectedDatabaseDeclaration = "DECLARE @ExpectedDatabase sysname = N'" +
            new SqlConnectionStringBuilder(fixture.Web.Database.ConnectionString).InitialCatalog + "';";
        foreach (var invalid in new[] {
            validEmpty.Replace("DECLARE @Apply bit = 1;", "DECLARE @Apply bit = NULL;"),
            validEmpty.Replace(expectedDatabaseDeclaration, "DECLARE @ExpectedDatabase sysname = NULL;"),
            validEmpty.Replace("DECLARE @GrantsJson nvarchar(max) = N'[]';", "DECLARE @GrantsJson nvarchar(max) = NULL;") })
        {
            await Assert.ThrowsAsync<SqlException>(() => fixture.Web.Database.ExecuteAsync(invalid));
            Assert.Equal(before, await SecuritySnapshotAsync());
        }
    }

    private string Delta(int storeId, bool apply, string grants)
    {
        var database = new SqlConnectionStringBuilder(fixture.Web.Database.ConnectionString).InitialCatalog;
        return File.ReadAllText(Path.Combine(FullApplicationFixture.SourceRoot(), "docs", "delivery-security-delta.sql"))
            .Replace("DECLARE @StoreId int = NULL;", "DECLARE @StoreId int = " + storeId + ";")
            .Replace("DECLARE @ExpectedDatabase sysname = N'';", "DECLARE @ExpectedDatabase sysname = N'" + database + "';")
            .Replace("DECLARE @Apply bit = 0;", "DECLARE @Apply bit = " + (apply ? "1" : "0") + ";")
            .Replace("DECLARE @GrantsJson nvarchar(max) = N'[]';", "DECLARE @GrantsJson nvarchar(max) = N'" + grants.Replace("'", "''") + "';");
    }
    private async Task<string> SecuritySnapshotAsync(int? storeId = null)
    {
        await using var db = fixture.Web.Database.CreateHostContext();
        var roles = db.Roles.IgnoreQueryFilters().AsNoTracking().Where(x => storeId == null || x.StoreId == storeId);
        return JsonSerializer.Serialize(new {
            Roles = await roles.OrderBy(x => x.Id).ToArrayAsync(),
            Menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().Where(x => storeId == null || x.StoreId == storeId).OrderBy(x => x.Id).ToArrayAsync(),
            Grants = await db.RolePermissions.AsNoTracking().Where(x => roles.Select(r => r.Id).Contains(x.RoleId)).OrderBy(x => x.RoleId).ThenBy(x => x.PermissionId).ToArrayAsync(),
            Permissions = storeId != null ? null : await db.Permissions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}
