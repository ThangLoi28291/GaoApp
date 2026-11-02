using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AdminMenuPinSqlServerTests
{
    [Fact]
    public async Task Pins_are_store_scoped_permission_filtered_concurrent_and_do_not_change_menu_or_grants()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var admin = await SecurityAdministrationPlanATests.MakeAdmin(app, await app.AddAccountAsync(store));
        var employee = await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View);
        var broadEmployee = await app.AddAccountAsync(store, "*");
        var foreignAdmin = await SecurityAdministrationPlanATests.MakeAdmin(app, await app.AddAccountAsync(app.Stores[1]));
        int rootId, disabledId, restrictedId, foreignId;
        int[] leafIds, grants;
        string stamp;
        Dictionary<int, string> menuVersions;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var root = new AdminMenuItem { StoreId = store.StoreId, Title = "PIN-Root", IsActive = true };
            db.Add(root); await db.SaveChangesAsync(); rootId = root.Id;
            var leaves = Enumerable.Range(0, 5).Select(i => new AdminMenuItem { StoreId = store.StoreId, ParentId = rootId,
                Title = "PIN-" + i, Controller = "Product", IsActive = true, SortOrder = i,
                PermissionCode = PermissionCodes.Catalog.Product.View }).ToArray();
            var disabled = new AdminMenuItem { StoreId = store.StoreId, Title = "PIN-Off", Controller = "Product", IsActive = false };
            var restricted = new AdminMenuItem { StoreId = store.StoreId, Title = "PIN-Restricted", Controller = "AdminMenus", IsActive = true,
                PermissionCode = PermissionCodes.Security.Role.Permissions };
            db.AddRange(leaves); db.AddRange(disabled, restricted); await db.SaveChangesAsync();
            leafIds = leaves.Select(x => x.Id).ToArray(); disabledId = disabled.Id; restrictedId = restricted.Id;
            grants = await db.RolePermissions.Where(x => x.RoleId == employee.RoleId).Select(x => x.PermissionId).Order().ToArrayAsync();
            var membership = await db.UserInStores.Include(x => x.User).SingleAsync(x => x.UserId == employee.UserId);
            stamp = AuthSessionStamp.Create(membership.User, membership);
            menuVersions = (await db.AdminMenuItems.AsNoTracking().ToListAsync()).ToDictionary(x => x.Id, x => Convert.ToHexString(x.RowVersion));
        }
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            var foreign = new AdminMenuItem { StoreId = app.Stores[1].StoreId, Title = "PIN-Foreign", Controller = "Product", IsActive = true };
            db.Add(foreign); await db.SaveChangesAsync(); foreignId = foreign.Id;
        }
        using var client = await app.LoginAsync(admin);
        using var employeeClient = await app.LoginAsync(employee);
        using var broadClient = await app.LoginAsync(broadEmployee);
        using var foreignClient = await app.LoginAsync(foreignAdmin);
        async Task<AdminMenuPinWorkspace> Read() => (await client.JsonAsync(HttpMethod.Get, "/admin/menu-pins/workspace"))
            .Deserialize<AdminMenuPinWorkspace>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Task<HttpResponseMessage> Save(AdminMenuPinWorkspace state, params int[] ids) => client.Http.PostAsJsonAsync("/admin/menu-pins/save", new { state.Version, MenuIds = ids });
        async Task<int[]> Configured(FullApplicationFixture.Client who) => (await who.JsonAsync(HttpMethod.Get, "/admin/menu-pins/configured"))
            .GetProperty("menuIds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var initial = await Read();
        Assert.Empty(initial.MenuIds);
        Assert.DoesNotContain(initial.Choices, x => x.Id == rootId || x.Id == disabledId || x.Id == foreignId);
        foreach (var invalid in new[] { leafIds, new[] { leafIds[0], leafIds[0] }, new[] { rootId }, new[] { disabledId }, new[] { foreignId }, new[] { -1 } })
        { using var response = await Save(initial, invalid); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        // Both first writers have the same snapshot; only one may create this store's row.
        var competing = await Task.WhenAll(Save(initial, leafIds[0]), Save(initial, leafIds[1]));
        try { Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, competing.Select(x => x.StatusCode).Order().ToArray()); }
        finally { foreach (var response in competing) response.Dispose(); }
        var state = await Read();
        int[] ordered = [leafIds[3], restrictedId, leafIds[1], leafIds[0]];
        using (var response = await Save(state, ordered)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Save(state)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ordered, await Configured(client));
        Assert.Equal(new[] { leafIds[3], leafIds[1], leafIds[0] }, await Configured(employeeClient));
        Assert.Empty(await Configured(foreignClient));
        using (var response = await employeeClient.Http.GetAsync("/admin/menu-pins/workspace")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await broadClient.Http.GetAsync("/admin/menu-pins/workspace")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await broadClient.Http.PostAsJsonAsync("/admin/menu-pins/save", new { state.Version, MenuIds = ordered })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await client.Http.GetAsync($"/admin/menu-pins/preview?type=role&id={foreignAdmin.RoleId}&menuIds={leafIds[0]}")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.Add(new AdminMenuVisibilitySetting { StoreId = store.StoreId, RoleId = employee.RoleId, HiddenMenuIdsJson = JsonSerializer.Serialize(new[] { rootId }) });
            await db.SaveChangesAsync();
        }
        Assert.Empty(await Configured(employeeClient));
        var rolePreview = await client.JsonAsync(HttpMethod.Get, $"/admin/menu-pins/preview?type=role&id={employee.RoleId}&menuIds={leafIds[0]}&menuIds={restrictedId}");
        Assert.Empty(rolePreview.GetProperty("visibleIds").EnumerateArray());
        using (var response = await Save(await Read())) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await Configured(client));
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await Save(await Read())) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.Set<AdminMenuPinSetting>().ToListAsync());
        Assert.Equal(grants, await check.RolePermissions.Where(x => x.RoleId == employee.RoleId).Select(x => x.PermissionId).Order().ToArrayAsync());
        var unchanged = await check.UserInStores.Include(x => x.User).SingleAsync(x => x.UserId == employee.UserId);
        Assert.Equal(stamp, AuthSessionStamp.Create(unchanged.User, unchanged));
        var currentMenus = (await check.AdminMenuItems.AsNoTracking().ToListAsync()).ToDictionary(x => x.Id, x => Convert.ToHexString(x.RowVersion));
        Assert.Equal(menuVersions.Count, currentMenus.Count);
        foreach (var entry in menuVersions) Assert.Equal(entry.Value, currentMenus[entry.Key]);
    }
}
