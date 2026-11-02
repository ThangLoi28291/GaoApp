using System.Net;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class UserInStoreProfileHttpTests
{
    [Fact]
    public async Task Host_admin_can_edit_own_profile_in_ui_without_changing_privileges_or_other_store_accounts()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var host = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Update);
        var manager = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Update);
        var otherHost = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Update);
        var foreign = await app.AddAccountAsync(app.Stores[1], PermissionCodes.Security.UserInStore.View);
        var viewer = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View);
        int mappingId, otherHostMappingId, foreignMappingId;
        string passwordHash;
        byte[] roleVersion;
        await using (var db = app.Database.CreateTenantContext(host.Store.StoreId))
        {
            var user = await db.Users.SingleAsync(x => x.Id == host.UserId);
            user.IsHostAdmin = true;
            (await db.Users.SingleAsync(x => x.Id == otherHost.UserId)).IsHostAdmin = true;
            // A different user sharing this role must keep their session after profile edits.
            (await db.UserInStores.SingleAsync(x => x.UserId == manager.UserId)).RoleId = host.RoleId;
            await db.SaveChangesAsync();
            mappingId = (await db.UserInStores.SingleAsync(x => x.UserId == host.UserId)).Id;
            otherHostMappingId = (await db.UserInStores.SingleAsync(x => x.UserId == otherHost.UserId)).Id;
            passwordHash = user.PasswordHash;
            roleVersion = (await db.Roles.SingleAsync(x => x.Id == host.RoleId)).RowVersion.ToArray();
        }
        await using (var db = app.Database.CreateTenantContext(foreign.Store.StoreId))
            foreignMappingId = (await db.UserInStores.SingleAsync(x => x.UserId == foreign.UserId)).Id;
        using var owner = await app.LoginAsync(host);
        using var managerClient = await app.LoginAsync(manager with { RoleId = host.RoleId });
        using var viewerClient = await app.LoginAsync(viewer);
        using var staleOwner = await app.LoginAsync(host);
        using var anonymous = app.Anonymous(host.Store);
        var endpoint = "/Admin/UserInStores/Edit";
        var listing = "/admin/employees/data?keyword=" + Uri.EscapeDataString(host.Name);
        var data = await owner.JsonAsync(HttpMethod.Get, listing);
        var item = data.GetProperty("items").EnumerateArray().Single();
        Assert.True(item.GetProperty("canEdit").GetBoolean());
        Assert.False(item.GetProperty("canResetPassword").GetBoolean());
        Assert.False(item.GetProperty("canToggleActive").GetBoolean());
        var otherView = await managerClient.JsonAsync(HttpMethod.Get, listing);
        Assert.False(otherView.GetProperty("items").EnumerateArray().Single().GetProperty("canEdit").GetBoolean());

        var html = await owner.Http.GetStringAsync(endpoint + "?id=" + mappingId);
        Assert.Contains("name=\"FullName\"", html);
        Assert.Contains("name=\"Email\"", html);
        Assert.DoesNotMatch(new Regex("<select[^>]*name=\"RoleId\""), html);
        Assert.DoesNotMatch(new Regex("<input[^>]*type=\"checkbox\"[^>]*name=\"IsActive\""), html);
        Assert.Contains("Gạo Mart", WebUtility.HtmlDecode(html));
        Assert.Contains("gaomart-mark.svg", html);

        foreach (var client in new[] { managerClient, viewerClient })
        {
            using var response = await client.Http.PostAsync(endpoint, Form(mappingId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await owner.Http.PostAsync(endpoint, Form(otherHostMappingId)))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await anonymous.Http.PostAsync(endpoint, Form(mappingId)))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var token = owner.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        owner.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await owner.Http.PostAsync(endpoint, Form(mappingId)))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        owner.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        foreach (var fields in new[] {
            Form(mappingId, roleId: manager.RoleId), Form(mappingId, active: false),
            Form(mappingId, fullName: ""), Form(mappingId, email: "invalid-address") })
        {
            using var response = await owner.Http.PostAsync(endpoint, fields);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var db = app.Database.CreateTenantContext(host.Store.StoreId);
            Assert.Equal("E2E test user", (await db.Users.SingleAsync(x => x.Id == host.UserId)).FullName);
        }
        using (var response = await owner.Http.PostAsync(endpoint, Form(foreignMappingId)))
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await owner.Http.PostAsync(endpoint, Form(mappingId)))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Nguyễn Thắng Lợi", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        await using (var db = app.Database.CreateTenantContext(host.Store.StoreId))
        {
            var user = await db.Users.SingleAsync(x => x.Id == host.UserId);
            var membership = await db.UserInStores.SingleAsync(x => x.Id == mappingId);
            Assert.Equal("Nguyễn Thắng Lợi", user.FullName);
            Assert.Equal("thangloi@example.test", user.Email);
            Assert.Equal("0901234567", membership.PhoneNumber);
            Assert.Equal("Quản lý", membership.PositionName);
            Assert.Equal("Thông tin đã cập nhật", membership.Note);
            Assert.Equal(host.Name, user.UserName);
            Assert.Equal(passwordHash, user.PasswordHash);
            Assert.True(user.IsHostAdmin);
            Assert.True(user.IsActive);
            Assert.True(membership.IsActive);
            Assert.Equal(host.RoleId, membership.RoleId);
            Assert.Equal(roleVersion, (await db.Roles.SingleAsync(x => x.Id == host.RoleId)).RowVersion);
            Assert.Equal("E2E test user", (await db.Users.SingleAsync(x => x.Id == otherHost.UserId)).FullName);
        }
        await using (var db = app.Database.CreateTenantContext(foreign.Store.StoreId))
            Assert.Equal("E2E test user", (await db.Users.SingleAsync(x => x.Id == foreign.UserId)).FullName);
        using (var response = await owner.Http.GetAsync(listing)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await managerClient.Http.GetAsync(listing)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await staleOwner.Http.GetAsync(listing)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var saved = await owner.JsonAsync(HttpMethod.Get, listing);
        Assert.Equal("Nguyễn Thắng Lợi", saved.GetProperty("items").EnumerateArray().Single().GetProperty("fullName").GetString());

        FormUrlEncodedContent Form(int id, int? roleId = null, bool active = true,
            string fullName = "  Nguyễn Thắng Lợi  ", string email = "thangloi@example.test") => new(new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["RoleId"] = (roleId ?? host.RoleId).ToString(),
            ["IsActive"] = active.ToString(), ["UpdateProfile"] = "true", ["FullName"] = fullName,
            ["Email"] = email, ["PhoneNumber"] = "0901234567", ["PositionName"] = "Quản lý",
            ["Note"] = "Thông tin đã cập nhật", ["UserName"] = "injected-name", ["IsHostAdmin"] = "false",
            ["PasswordHash"] = "injected-value"
        });
    }
}
