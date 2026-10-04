using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class RolePermissionEditorSqlServerTests
{
    [Fact]
    public void Catalog_has_Vietnamese_presentation_without_changing_permission_codes()
    {
        foreach (var permission in PermissionCatalog.All)
        {
            Assert.NotEqual(permission.GroupName, PermissionDisplayNames.Group(permission.GroupName));
            Assert.NotEqual("Chức năng khác", PermissionDisplayNames.Feature(permission.Module, permission.Entity));
            var name = PermissionDisplayNames.Permission(permission.Code, "ignored");
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.DoesNotMatch(@"(?i)\b(permission|role|user|store|refund|terminal|integration|valuation|issue|background|audit)\b", name);
        }
        Assert.Equal("Quyền bổ sung", PermissionDisplayNames.Permission("custom.feature.action", "Quyền bổ sung"));
        Assert.Equal("Danh mục & sản phẩm", PermissionDisplayNames.Group("Catalog"));
    }

    [Fact]
    public async Task Editor_preserves_tenant_authorization_antiforgery_and_rejects_invalid_grants()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var admin = await app.AddAccountAsync(store, "*");
        var employee = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var foreign = await app.AddAccountAsync(app.Stores[1], PermissionCodes.Pos.Order.View);
        int roleId, original;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var role = new Role { StoreId = store.StoreId, Name = "Vai trò thử phân quyền", Code = "permission_editor_test" };
            original = await db.Permissions.Where(p => p.Code == PermissionCodes.System.ProductLabel.Print).Select(p => p.Id).SingleAsync();
            db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = original });
            await db.SaveChangesAsync(); roleId = role.Id;
        }
        using var client = await app.LoginAsync(admin);
        var landing = await client.Http.GetStringAsync("/Admin/RolePermissions");
        Assert.Contains("rolePicker", landing);
        Assert.DoesNotContain($"value=\"{foreign.RoleId}\"", landing);
        var matrix = await client.Http.GetStringAsync($"/Admin/RolePermissions?roleId={roleId}");
        Assert.Contains("SelectedPermissionIds", matrix);
        Assert.Contains("system.productlabel.print", matrix);
        async Task<HttpResponseMessage> Post(int id, string permission) => await client.Http.PostAsync("/Admin/RolePermissions/Save",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["RoleId"] = id.ToString(), ["SelectedPermissionIds"] = permission }));
        // Tampered ID and malformed form must never turn a validation failure into removal of existing grants.
        using var invalid = await Post(roleId, int.MaxValue.ToString()); Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        using var malformed = await Post(roleId, "not-a-number"); Assert.Equal(HttpStatusCode.Redirect, malformed.StatusCode);
        using var foreignSave = await Post(foreign.RoleId, original.ToString()); Assert.Equal(HttpStatusCode.Redirect, foreignSave.StatusCode);
        client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var noToken = await Post(roleId, original.ToString()); Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var denied = await app.LoginAsync(employee);
        using var forbidden = await denied.Http.PostAsync("/Admin/RolePermissions/Save", new FormUrlEncodedContent(new Dictionary<string, string> { ["RoleId"] = roleId.ToString() }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenGet = await denied.Http.GetAsync($"/Admin/RolePermissions?roleId={roleId}"); Assert.Equal(HttpStatusCode.Forbidden, forbiddenGet.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(new[] { original }, await check.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.PermissionId).ToArrayAsync());
        await using var other = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        Assert.Equal(new[] { PermissionCodes.Pos.Order.View }, await other.RolePermissions.Where(p => p.RoleId == foreign.RoleId).Select(p => p.Permission.Code).ToArrayAsync());
    }
}
