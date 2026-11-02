using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class SecurityAdministrationPlanATests
{
    [Fact]
    public async Task Broad_employee_permissions_do_not_authorize_security_administration()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var manager = await app.AddAccountAsync(app.Stores[0], "*");
        var target = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View);
        using var client = await app.LoginAsync(manager);
        int mappingId, selfId, adminRole;
        string hash;
        await using (var db = app.Database.CreateTenantContext(manager.Store.StoreId))
        {
            mappingId = await db.UserInStores.Where(x => x.UserId == target.UserId).Select(x => x.Id).SingleAsync();
            selfId = await db.UserInStores.Where(x => x.UserId == manager.UserId).Select(x => x.Id).SingleAsync();
            adminRole = await db.Roles.Where(x => x.Code == "ADMIN" && x.IsSystemRole).Select(x => x.Id).SingleAsync();
            hash = await db.Users.Where(x => x.Id == target.UserId).Select(x => x.PasswordHash).SingleAsync();
        }
        var forbidden = new (string Route, Dictionary<string, string> Fields)[]
        {
            ("CreateEmployee", new() { ["UserName"] = "forged_" + Guid.NewGuid().ToString("N"), ["FullName"] = "Forged", ["Password"] = "Long-forged-passphrase!", ["RoleId"] = target.RoleId.ToString(), ["IsActive"] = "true" }),
            ("Edit", Edit(selfId, adminRole)),
            ("Edit", Edit(mappingId, manager.RoleId)),
            ("ResetPassword", new() { ["UserId"] = target.UserId.ToString(), ["NewPassword"] = "Long-forged-passphrase!", ["ConfirmPassword"] = "Long-forged-passphrase!" }),
            ("ToggleActive", new() { ["Id"] = mappingId.ToString(), ["Reason"] = "Forged lock" }),
            ("Delete", new() { ["Id"] = mappingId.ToString(), ["Reason"] = "Forged delete" })
        };
        foreach (var (route, fields) in forbidden)
        {
            using var response = await Post(client, "/Admin/UserInStores/" + route, fields);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await Post(client, "/Admin/RolePermissions/Save", new() { ["RoleId"] = target.RoleId.ToString() }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Post(client, "/Admin/AdminMenus/Create", new() { ["Title"] = "Forged menu", ["PermissionCode"] = PermissionCodes.Security.Role.Permissions, ["AssignRoleIds"] = target.RoleId.ToString() }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // HR-only changes remain available without password confirmation.
        var profile = Edit(mappingId, target.RoleId);
        profile["PositionName"] = "Trưởng ca";
        using (var response = await Post(client, "/Admin/UserInStores/Edit", profile))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var check = app.Database.CreateTenantContext(manager.Store.StoreId);
        var membership = await check.UserInStores.SingleAsync(x => x.Id == mappingId);
        Assert.Equal(target.RoleId, membership.RoleId); Assert.True(membership.IsActive); Assert.False(membership.IsDeleted);
        Assert.Equal("Trưởng ca", membership.PositionName);
        Assert.Equal(hash, await check.Users.Where(x => x.Id == target.UserId).Select(x => x.PasswordHash).SingleAsync());
        Assert.False(await check.AdminMenuItems.AnyAsync(x => x.Title == "Forged menu"));
    }

    [Fact]
    public async Task Verified_admin_requires_confirmation_and_revokes_sessions_without_logging_credentials()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await MakeAdmin(app, await app.AddAccountAsync(app.Stores[0], "*"));
        var target = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View);
        var newRole = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View);
        using var client = await app.LoginAsync(admin);
        using var stale = await app.LoginAsync(target);
        int mappingId;
        await using (var db = app.Database.CreateTenantContext(admin.Store.StoreId))
            mappingId = await db.UserInStores.Where(x => x.UserId == target.UserId).Select(x => x.Id).SingleAsync();
        var fields = Edit(mappingId, newRole.RoleId);
        using (var response = await Post(client, "/Admin/UserInStores/Edit", fields))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        fields["Reason"] = "Phân công công việc mới";
        fields["AdministratorPassword"] = "Wrong-admin-password!";
        using (var response = await Post(client, "/Admin/UserInStores/Edit", fields))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        fields["AdministratorPassword"] = admin.Password;
        using (var response = await Post(client, "/Admin/UserInStores/Edit", fields))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await stale.Http.GetAsync("/admin/employees/data"))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var employeeName = "plan_a_" + Guid.NewGuid().ToString("N");
        const string createdPassword = "Created-employee-password!";
        using (var response = await Post(client, "/Admin/UserInStores/CreateEmployee", new()
        {
            ["UserName"] = employeeName, ["FullName"] = "Nhân viên thử nghiệm", ["Password"] = createdPassword,
            ["RoleId"] = newRole.RoleId.ToString(), ["IsActive"] = "true"
        })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        int createdMapping, createdUser;
        await using (var db = app.Database.CreateTenantContext(admin.Store.StoreId))
        {
            var mapping = await db.UserInStores.Include(x => x.User).SingleAsync(x => x.User.UserName == employeeName);
            Assert.False(mapping.User.IsHostAdmin); Assert.Equal(newRole.RoleId, mapping.RoleId);
            createdMapping = mapping.Id; createdUser = mapping.UserId;
        }
        using var createdSession = await app.LoginAsync(new(admin.Store, createdUser, newRole.RoleId, employeeName, createdPassword));
        using (var response = await Post(client, "/Admin/UserInStores/Delete", new()
        {
            ["Id"] = createdMapping.ToString(), ["Reason"] = "Kết thúc nhân viên thử nghiệm"
        })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await createdSession.Http.GetAsync("/admin/employees/data"))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var beforeReset = await app.LoginAsync(target);
        const string newPassword = "Replacement-password-for-test!";
        using (var response = await Post(client, "/Admin/UserInStores/ResetPassword", new()
        {
            ["UserId"] = target.UserId.ToString(), ["NewPassword"] = newPassword, ["ConfirmPassword"] = newPassword,
            ["AdministratorPassword"] = admin.Password, ["Reason"] = "Nhân viên quên mật khẩu"
        })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await beforeReset.Http.GetAsync("/admin/employees/data"))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var afterReset = await app.LoginAsync(target with { Password = newPassword });
        using (var response = await afterReset.Http.GetAsync("/admin/employees/data"))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Post(client, "/Admin/RolePermissions/Save", new()
        {
            ["RoleId"] = newRole.RoleId.ToString(), ["AdministratorPassword"] = admin.Password, ["Reason"] = "Thu hồi quyền của vai trò"
        })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using (var response = await afterReset.Http.GetAsync("/admin/employees/data"))
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var check = app.Database.CreateTenantContext(admin.Store.StoreId);
        Assert.Equal(newRole.RoleId, await check.UserInStores.Where(x => x.Id == mappingId).Select(x => x.RoleId).SingleAsync());
        var audits = await check.AuditLogs.Where(x => x.ActorUserId == admin.UserId && x.Module == GaoApp.Domain.Enums.AuditModuleType.Security).ToListAsync();
        Assert.Contains(audits, x => x.Summary!.Contains("Phân công công việc mới") && x.OldValuesJson!.Contains("RoleId"));
        Assert.Contains(audits, x => x.Summary!.Contains("Nhân viên quên mật khẩu"));
        foreach (var audit in audits)
        {
            var contents = audit.Summary + audit.OldValuesJson + audit.NewValuesJson;
            Assert.DoesNotContain(admin.Password, contents); Assert.DoesNotContain(newPassword, contents);
            Assert.DoesNotContain(createdPassword, contents);
            Assert.DoesNotContain("PasswordHash", contents); Assert.DoesNotContain("AdministratorPassword", contents);
        }
    }

    [Fact]
    public async Task Store_admin_cannot_manage_ADMIN_self_or_shared_credentials_and_menu_grants_use_same_rules()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var admin = await MakeAdmin(app, await app.AddAccountAsync(app.Stores[0]));
        var peer = await MakeAdmin(app, await app.AddAccountAsync(app.Stores[0]));
        var shared = await app.AddAccountAsync(app.Stores[1]);
        var ordinary = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Security.UserInStore.View);
        int selfId, peerId, sharedId, permissionId;
        await using (var db = app.Database.CreateTenantContext(admin.Store.StoreId))
        {
            db.UserInStores.Add(new UserInStore { StoreId = admin.Store.StoreId, UserId = shared.UserId, RoleId = ordinary.RoleId });
            await db.SaveChangesAsync();
            selfId = await db.UserInStores.Where(x => x.UserId == admin.UserId).Select(x => x.Id).SingleAsync();
            peerId = await db.UserInStores.Where(x => x.UserId == peer.UserId).Select(x => x.Id).SingleAsync();
            sharedId = await db.UserInStores.Where(x => x.UserId == shared.UserId).Select(x => x.Id).SingleAsync();
            permissionId = await db.Permissions.Where(x => x.Code == PermissionCodes.Security.Role.Permissions).Select(x => x.Id).SingleAsync();
        }
        using var client = await app.LoginAsync(admin);
        foreach (var id in new[] { selfId, peerId })
        {
            var fields = Edit(id, ordinary.RoleId);
            fields["AdministratorPassword"] = admin.Password; fields["Reason"] = "Thay đổi trái phép";
            using var response = await Post(client, "/Admin/UserInStores/Edit", fields);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using (var response = await Post(client, "/Admin/UserInStores/Delete", new() { ["Id"] = peerId.ToString(), ["Reason"] = "Xóa ADMIN khác" }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Post(client, "/Admin/UserInStores/ResetPassword", new()
        {
            ["UserId"] = peer.UserId.ToString(), ["NewPassword"] = "New-admin-passphrase!", ["ConfirmPassword"] = "New-admin-passphrase!",
            ["AdministratorPassword"] = admin.Password, ["Reason"] = "Đổi mật khẩu ADMIN khác"
        })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Post(client, "/Admin/UserInStores/ResetPassword", new()
        {
            ["UserId"] = shared.UserId.ToString(), ["NewPassword"] = "New-shared-passphrase!", ["ConfirmPassword"] = "New-shared-passphrase!",
            ["AdministratorPassword"] = admin.Password, ["Reason"] = "Đổi mật khẩu dùng chung"
        })) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); // Rejected with a validation toast.
        using (var response = await Post(client, "/Admin/AdminMenus/Create", new()
        {
            ["Title"] = "Không được gỡ quyền ADMIN", ["PermissionCode"] = PermissionCodes.Security.Role.Permissions,
            ["AssignRoleIds"] = ordinary.RoleId.ToString(), ["AdministratorPassword"] = admin.Password, ["Reason"] = "Thử đường phân quyền qua menu"
        })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var check = app.Database.CreateTenantContext(admin.Store.StoreId);
        Assert.Equal(admin.RoleId, await check.UserInStores.Where(x => x.Id == selfId).Select(x => x.RoleId).SingleAsync());
        Assert.Equal(peer.RoleId, await check.UserInStores.Where(x => x.Id == peerId).Select(x => x.RoleId).SingleAsync());
        Assert.True(await check.RolePermissions.AnyAsync(x => x.RoleId == admin.RoleId && x.PermissionId == permissionId));
        Assert.False(await check.AdminMenuItems.AnyAsync(x => x.Title == "Không được gỡ quyền ADMIN"));
        using var unchangedShared = await app.LoginAsync(shared);
        Assert.True(sharedId > 0);
    }

    [Fact]
    public async Task Concurrent_host_actions_preserve_last_admin_and_essential_grants()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var host = await app.AddAccountAsync(app.Stores[0], "*");
        var first = await MakeAdmin(app, await app.AddAccountAsync(app.Stores[0]));
        var second = await MakeAdmin(app, await app.AddAccountAsync(app.Stores[0]));
        int[] mappingIds;
        await using (var db = app.Database.CreateTenantContext(host.Store.StoreId))
        {
            (await db.Users.SingleAsync(x => x.Id == host.UserId)).IsHostAdmin = true;
            await db.SaveChangesAsync();
            mappingIds = await db.UserInStores.Where(x => x.UserId == first.UserId || x.UserId == second.UserId).Select(x => x.Id).ToArrayAsync();
        }
        using var client1 = await app.LoginAsync(host);
        using var client2 = await app.LoginAsync(host);
        var results = await Task.WhenAll(Post(client1, "/Admin/UserInStores/ToggleActive", new() { ["Id"] = mappingIds[0].ToString(), ["Reason"] = "Kết thúc công việc" }),
            Post(client2, "/Admin/UserInStores/ToggleActive", new() { ["Id"] = mappingIds[1].ToString(), ["Reason"] = "Kết thúc công việc" }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Redirect);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        foreach (var response in results) response.Dispose();
        int lastId;
        List<int> permissionIds;
        await using (var check = app.Database.CreateTenantContext(host.Store.StoreId))
        {
            var admins = await check.UserInStores.Where(x => x.IsActive && !x.IsDeleted && x.Role.IsSystemRole && x.Role.Code == "ADMIN").ToListAsync();
            lastId = Assert.Single(admins).Id;
            permissionIds = await check.RolePermissions.Where(x => x.RoleId == first.RoleId).Select(x => x.PermissionId).ToListAsync();
        }
        using (var response = await Post(client1, "/Admin/UserInStores/Delete", new() { ["Id"] = lastId.ToString(), ["Reason"] = "Xóa ADMIN cuối" }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await Post(client1, "/Admin/RolePermissions/Save", new() { ["RoleId"] = first.RoleId.ToString(), ["AdministratorPassword"] = host.Password, ["Reason"] = "Gỡ quyền ADMIN" }))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await Post(client1, "/Admin/AdminMenus/Create", new()
        {
            ["Title"] = "Gỡ quyền thiết yếu qua menu", ["PermissionCode"] = PermissionCodes.Security.Role.Permissions,
            ["AssignRoleIds"] = host.RoleId.ToString(), ["AdministratorPassword"] = host.Password, ["Reason"] = "Thử gỡ quyền qua menu"
        })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var final = app.Database.CreateTenantContext(host.Store.StoreId);
        Assert.True(await final.UserInStores.AnyAsync(x => x.Id == lastId && !x.IsDeleted && x.IsActive));
        Assert.False(await final.AdminMenuItems.AnyAsync(x => x.Title == "Gỡ quyền thiết yếu qua menu"));
        Assert.Equal(permissionIds.Order(), (await final.RolePermissions.Where(x => x.RoleId == first.RoleId).Select(x => x.PermissionId).ToListAsync()).Order());
        Assert.Single(await final.AuditLogs.Where(x => x.Module == GaoApp.Domain.Enums.AuditModuleType.Security && x.Summary!.Contains("Kết thúc công việc")).ToListAsync());
        using (var response = await Post(client1, "/Admin/Roles/Edit", new()
        {
            ["Id"] = first.RoleId.ToString(), ["Code"] = "admin", ["Name"] = "Quản trị cửa hàng"
        })) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ADMIN", await final.Roles.AsNoTracking().Where(x => x.Id == first.RoleId).Select(x => x.Code).SingleAsync());
    }

    private static Dictionary<string, string> Edit(int id, int roleId) => new()
    {
        ["Id"] = id.ToString(), ["RoleId"] = roleId.ToString(), ["IsActive"] = "true", ["UpdateProfile"] = "false"
    };

    private static Task<HttpResponseMessage> Post(FullApplicationFixture.Client client, string route, Dictionary<string, string> fields)
        => client.Http.PostAsync(route, new FormUrlEncodedContent(fields));

    internal static async Task<FullApplicationFixture.Account> MakeAdmin(FullApplicationFixture app, FullApplicationFixture.Account account)
    {
        await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
        var roleId = await db.Roles.Where(x => x.IsSystemRole && x.Code == "ADMIN").Select(x => x.Id).SingleAsync();
        (await db.UserInStores.SingleAsync(x => x.UserId == account.UserId)).RoleId = roleId;
        await db.SaveChangesAsync();
        return account with { RoleId = roleId };
    }
}
