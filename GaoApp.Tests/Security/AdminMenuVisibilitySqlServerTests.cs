using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.AdminMenus;
using GaoApp.Infrastructure.Repositories.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AdminMenuVisibilitySqlServerTests
{
    [Fact]
    public void Visibility_prunes_hidden_ancestors_empty_folders_and_unavailable_children()
    {
        MenuVisibilityNode[] menus = [new(1, null, "Catalog", "", true, null, true), new(2, 1, "Product", "", true, null, false),
            new(3, 1, "No permission", "", false, "denied", false), new(4, null, "Empty", "", true, null, true)];
        Assert.Equal(new[] { 1, 2 }, MenuVisibilityRules.VisibleIds(menus, new HashSet<int>()).Order().ToArray());
        Assert.Empty(MenuVisibilityRules.VisibleIds(menus, new HashSet<int> { 1 }));
        Assert.Empty(MenuVisibilityRules.VisibleIds(menus, new HashSet<int> { 2 }));
    }

    [Fact]
    public async Task Bulk_visibility_inherits_overrides_resets_and_never_changes_permissions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var admin = await app.AddAccountAsync(store, "*");
        var employee = await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View);
        var peer = await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View);
        var foreign = await app.AddAccountAsync(app.Stores[1], "*");
        int memberId, rootId, productId, deniedId, foreignMenuId;
        int[] originalGrants;
        string originalStamp;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var member = await db.UserInStores.Include(x => x.User).Include(x => x.Role).SingleAsync(x => x.UserId == employee.UserId); memberId = member.Id;
            originalStamp = AuthSessionStamp.Create(member.User, member);
            (await db.UserInStores.SingleAsync(x => x.UserId == peer.UserId)).RoleId = employee.RoleId;
            var root = new AdminMenuItem { StoreId = store.StoreId, Title = "MV-Catalog" };
            db.AdminMenuItems.Add(root); await db.SaveChangesAsync(); rootId = root.Id;
            var product = new AdminMenuItem { StoreId = store.StoreId, ParentId = rootId, Title = "MV-Products", Controller = "Product", PermissionCode = PermissionCodes.Catalog.Product.View };
            var denied = new AdminMenuItem { StoreId = store.StoreId, ParentId = rootId, Title = "MV-Security", Controller = "AdminMenus", PermissionCode = PermissionCodes.Security.Role.Permissions };
            db.AdminMenuItems.AddRange(product, denied); await db.SaveChangesAsync(); productId = product.Id; deniedId = denied.Id;
            originalGrants = await db.RolePermissions.Where(x => x.RoleId == employee.RoleId).Select(x => x.PermissionId).OrderBy(x => x).ToArrayAsync();
        }
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            var item = new AdminMenuItem { StoreId = app.Stores[1].StoreId, Title = "ForeignMenu", Controller = "Product" };
            db.AdminMenuItems.Add(item); await db.SaveChangesAsync(); foreignMenuId = item.Id;
        }
        using var client = await app.LoginAsync(admin);
        using var foreignClient = await app.LoginAsync(foreign);
        var foreignPage = await foreignClient.Http.GetStringAsync("/Admin/AdminMenuVisibility");
        Assert.Contains("ForeignMenu", foreignPage);
        Assert.DoesNotContain("MV-Products", foreignPage);
        async Task<MenuVisibilityWorkspace> Read(string type, int id) => (await client.JsonAsync(HttpMethod.Get, $"/Admin/AdminMenuVisibility/Detail?type={type}&id={id}")).Deserialize<MenuVisibilityWorkspace>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        async Task<HttpResponseMessage> Save(MenuVisibilityWorkspace s, int[] hidden, bool reset = false) => await client.Http.PostAsJsonAsync("/Admin/AdminMenuVisibility/Save", new { s.Type, s.Id, s.Version, HiddenIds = hidden, Reset = reset });
        async Task<int[]> Render(int userId)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var permissions = new CurrentStorePermissionService(new UserInStoreRepository(db));
            var service = new AdminMenuService(new AdminMenuRepository(db), null!, null!, permissions, new AdminMenuVisibilityService(db));
            var tree = await service.GetForRenderAsync(store.StoreId, userId);
            return tree.SelectMany(x => new[] { x.Id }.Concat(x.Children.Select(c => c.Id))).ToArray();
        }
        var landing = await client.Http.GetStringAsync("/Admin/AdminMenuVisibility");
        Assert.Contains("mvSubjects", landing); Assert.DoesNotContain($"data-type=\"role\" data-id=\"{foreign.RoleId}\"", landing);
        var roleState = await Read("role", employee.RoleId);
        Assert.False(roleState.Menus.Single(x => x.Id == deniedId).Available);
        using (var invalid = await Save(roleState, [foreignMenuId])) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await client.Http.GetAsync($"/Admin/AdminMenuVisibility/Detail?type=role&id={foreign.RoleId}")) Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        using (var invalid = await client.Http.PostAsJsonAsync("/Admin/AdminMenuVisibility/Save", new { Type = "role", Id = foreign.RoleId, roleState.Version, HiddenIds = Array.Empty<int>() })) Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        using (var saved = await Save(roleState, [productId])) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using (var stale = await Save(roleState, [])) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.DoesNotContain(productId, await Render(employee.UserId)); Assert.DoesNotContain(rootId, await Render(employee.UserId));
        Assert.DoesNotContain(productId, await Render(peer.UserId));
        var personal = await Read("employee", memberId); Assert.True(personal.Inherited);
        using (var saved = await Save(personal, [])) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains(productId, await Render(employee.UserId)); Assert.DoesNotContain(deniedId, await Render(employee.UserId));
        roleState = await Read("role", employee.RoleId); Assert.Equal(1, roleState.PersonalCount);
        using (var saved = await Save(roleState, [rootId])) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains(productId, await Render(employee.UserId)); Assert.DoesNotContain(productId, await Render(peer.UserId));
        personal = await Read("employee", memberId);
        using (var saved = await Save(personal, [], true)) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.True((await Read("employee", memberId)).Inherited); Assert.DoesNotContain(productId, await Render(employee.UserId));
        using var employeeClient = await app.LoginAsync(employee);
        using (var forbidden = await employeeClient.Http.GetAsync("/Admin/AdminMenuVisibility")) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var forbidden = await employeeClient.Http.PostAsJsonAsync("/Admin/AdminMenuVisibility/Save", new { personal.Type, personal.Id, personal.Version, HiddenIds = Array.Empty<int>() })) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var accessible = await employeeClient.Http.GetAsync("/Admin/Product")) Assert.Equal(HttpStatusCode.OK, accessible.StatusCode);
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var noToken = await Save(await Read("role", employee.RoleId), [])) Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        var unchangedMember = await check.UserInStores.Include(x => x.User).Include(x => x.Role).SingleAsync(x => x.Id == memberId);
        Assert.Equal(originalStamp, AuthSessionStamp.Create(unchangedMember.User, unchangedMember));
        Assert.Equal(originalGrants, await check.RolePermissions.Where(x => x.RoleId == employee.RoleId).Select(x => x.PermissionId).OrderBy(x => x).ToArrayAsync());
        var auth = new CurrentStorePermissionService(new UserInStoreRepository(check));
        Assert.True(await auth.HasPermissionAsync(store.StoreId, employee.UserId, PermissionCodes.Catalog.Product.View));
        Assert.False(await auth.HasPermissionAsync(store.StoreId, employee.UserId, PermissionCodes.Security.Role.Permissions));
    }
}
