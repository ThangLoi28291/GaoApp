using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.AdminMenus;
using GaoApp.Application.DTOs.Security;
using GaoApp.Application.DTOs.Security.Permissions;
using GaoApp.Application.DTOs.Security.UserInStores;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.AdminMenus;
using GaoApp.Application.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Repositories.AdminMenus;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Repositories.Users;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

public sealed class StandardSecurityDataFactAttribute : FactAttribute
{
    public StandardSecurityDataFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GAOAPP_SECURITY_STANDARD_SOURCE") != "GaoAppDb")
            Skip = "Select GAOAPP_SECURITY_STANDARD_SOURCE=GaoAppDb to run against a read-only copy of the local standard dataset.";
    }
}

[Collection("SqlServerConcurrency")]
public sealed class SecurityAdministrationStandardDataTests(ITestOutputHelper output)
{
    [StandardSecurityDataFact]
    public async Task Standard_dataset_preserves_all_security_rows_and_existing_administrators_remain_authorized()
    {
        await using var fixture = new InventoryPostingLocalDb();
        var counts = await fixture.CopySecurityStandardDataAsync();
        output.WriteLine("Source: local GaoAppDb; seven security tables copied and SHA-256 verified (generated rowversions excluded).");
        foreach (var item in counts) output.WriteLine($"{item.Key}: {item.Value}");
        Assert.True(counts["Users"] > 0 && counts["UserInStores"] > 0 && counts["AdminMenuItems"] > 0);
        await using var host = fixture.CreateHostContext();
        var storeIds = await host.Stores.Where(x => x.IsActive && !x.IsDeleted).Select(x => x.Id).ToListAsync();
        Assert.NotEmpty(storeIds);
        foreach (var storeId in storeIds)
        {
            await using var db = fixture.CreateTenantContext(storeId);
            var members = await db.UserInStores.AsNoTracking().Include(x => x.User).Include(x => x.Role)
                .Where(x => x.IsActive && !x.IsDeleted && x.User.IsActive && !x.User.IsDeleted &&
                    !x.Role.IsDeleted && x.Role.StoreId == storeId).ToListAsync();
            Assert.Contains(members, x => x.Role.IsSystemRole && x.Role.Code == "ADMIN");
            foreach (var member in members)
            {
                var services = Services(db, storeId, member.UserId);
                var actor = await services.Guard.GetActorAsync(storeId, member.UserId);
                Assert.Equal(member.User.IsHostAdmin, actor.IsHostAdmin);
                Assert.Equal(member.Role.IsSystemRole && member.Role.Code == "ADMIN", actor.IsStoreAdmin);
                if (!actor.IsStoreAdmin && !actor.IsHostAdmin) continue;
                string[] required = [PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Create,
                    PermissionCodes.Security.UserInStore.Update, PermissionCodes.Security.Role.View, PermissionCodes.Security.Role.Permissions];
                foreach (var permission in required)
                {
                    await using var scope = await services.Guard.BeginAsync(storeId, member.UserId, permission);
                    scope.RequireAdministrator();
                }
            }
            Assert.Equal(counts["AdminMenuItems"], await host.AdminMenuItems.LongCountAsync());
            output.WriteLine($"Store {storeId}: {members.Count} active accounts classified from persisted user/membership/role; existing ADMIN authorization passed.");
        }
    }

    [StandardSecurityDataFact]
    public async Task Standard_roles_enforce_employee_boundaries_confirmation_audit_revocation_and_last_admin_concurrency()
    {
        await using var fixture = new InventoryPostingLocalDb();
        var counts = await fixture.CopySecurityStandardDataAsync();
        int storeId, adminRoleId, ordinaryRoleId, alternativeRoleId, adminId, hostId, hrId, hrMappingId, hrRoleId;
        const string administratorPassword = "Standard-data-admin-passphrase!";
        const string hostPassword = "Standard-data-host-passphrase!";
        const string employeePassword = "Standard-data-employee-passphrase!";
        const string replacementPassword = "Standard-data-replacement-passphrase!";
        var hasher = new PasswordHasher();
        await using (var db = fixture.CreateHostContext())
        {
            storeId = await db.Stores.Where(x => x.IsActive && !x.IsDeleted).Select(x => x.Id).FirstAsync();
            adminRoleId = await db.Roles.Where(x => x.StoreId == storeId && x.IsSystemRole && x.Code == "ADMIN" && !x.IsDeleted).Select(x => x.Id).SingleAsync();
            var normalRoles = await db.Roles.Where(x => x.StoreId == storeId && x.Code != "ADMIN" && !x.IsDeleted).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
            Assert.True(normalRoles.Count >= 2);
            ordinaryRoleId = normalRoles[0]; alternativeRoleId = normalRoles[1];
            var admin = NewUser(administratorPassword); var host = NewUser(hostPassword); host.IsHostAdmin = true;
            var hr = NewUser(employeePassword);
            var hrRole = new Role { StoreId = storeId, Code = "QA_HR_" + Guid.NewGuid().ToString("N"), Name = "ADMIN", IsSystemRole = false };
            var hrMapping = new UserInStore { StoreId = storeId, User = hr, Role = hrRole, PositionName = "ADMIN" };
            db.UserInStores.AddRange(new UserInStore { StoreId = storeId, User = admin, RoleId = adminRoleId },
                new UserInStore { StoreId = storeId, User = host, RoleId = ordinaryRoleId }, hrMapping);
            string[] codes = [PermissionCodes.Security.UserInStore.View, PermissionCodes.Security.UserInStore.Create,
                PermissionCodes.Security.UserInStore.Update, PermissionCodes.Security.UserInStore.Delete, PermissionCodes.Security.Role.Permissions];
            var permissions = await db.Permissions.Where(x => codes.Contains(x.Code)).Select(x => x.Id).ToListAsync();
            Assert.Equal(codes.Length, permissions.Count);
            foreach (var permission in permissions) db.RolePermissions.Add(new RolePermission { Role = hrRole, PermissionId = permission });
            await db.SaveChangesAsync();
            adminId = admin.Id; hostId = host.Id; hrId = hr.Id; hrMappingId = hrMapping.Id; hrRoleId = hrRole.Id;
        }
        int targetId, targetUserId;
        var revoked = new Revocations();
        await using (var db = fixture.CreateTenantContext(storeId))
        {
            var admin = Services(db, storeId, adminId, revoked);
            var created = await admin.Users.CreateEmployeeAsync(storeId, adminId, new CreateEmployeeInStoreRequest
            {
                UserName = "standard_" + Guid.NewGuid().ToString("N"), FullName = "Acceptance employee",
                RoleId = ordinaryRoleId, IsActive = true, Password = employeePassword
            });
            Assert.True(created.Success, created.ErrorMessage); targetId = created.Id!.Value;
            targetUserId = await db.UserInStores.Where(x => x.Id == targetId).Select(x => x.UserId).SingleAsync();
        }
        await using (var db = fixture.CreateTenantContext(storeId))
        {
            var hr = Services(db, storeId, hrId);
            Assert.False((await hr.Guard.GetActorAsync(storeId, hrId)).CanAdminister);
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Users.UpdateAsync(storeId, hrId, Edit(hrMappingId, adminRoleId)));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Users.UpdateAsync(storeId, hrId, Edit(targetId, alternativeRoleId)));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Users.ToggleActiveAsync(storeId, targetId, hrId, confirmation: Proof(null)));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Users.DeleteAsync(storeId, targetId, hrId, confirmation: Proof(null)));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Users.ResetPasswordAsync(storeId, targetUserId, replacementPassword, hrId, confirmation: Proof(null)));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Grants.SaveAsync(storeId, hrId, new SaveRolePermissionsRequest { RoleId = ordinaryRoleId }));
            await Assert.ThrowsAsync<ForbiddenAppException>(() => hr.Menus.CreateAsync(storeId, hrId, new SaveAdminMenuItemRequest
            {
                Title = "Forbidden standard data menu", PermissionCode = PermissionCodes.Security.Role.Permissions, AssignRoleIds = [hrRoleId]
            }));
            var edit = Edit(targetId, ordinaryRoleId); edit.PositionName = "Trưởng ca";
            Assert.True((await hr.Users.UpdateAsync(storeId, hrId, edit)).Success);
        }
        await using (var db = fixture.CreateTenantContext(storeId))
        {
            var admin = Services(db, storeId, adminId, revoked);
            var membership = await db.UserInStores.AsNoTracking().Include(x => x.User).Include(x => x.Role).SingleAsync(x => x.Id == targetId);
            var previous = AuthSessionStamp.Create(membership.User, membership);
            await Assert.ThrowsAsync<ValidationAppException>(() => admin.Users.ResetPasswordAsync(storeId, targetUserId, replacementPassword, adminId, confirmation: Proof("Wrong-password!")));
            var edit = Edit(targetId, alternativeRoleId); edit.AdministratorPassword = administratorPassword; edit.Reason = "Điều chỉnh phân công thử nghiệm";
            Assert.True((await admin.Users.UpdateAsync(storeId, adminId, edit)).Success);
            membership = await db.UserInStores.AsNoTracking().Include(x => x.User).Include(x => x.Role).SingleAsync(x => x.Id == targetId);
            Assert.False(AuthSessionStamp.Matches(previous, AuthSessionStamp.Create(membership.User, membership)));
            Assert.True((await admin.Users.ResetPasswordAsync(storeId, targetUserId, replacementPassword, adminId, confirmation: Proof(administratorPassword))).Success);
            Assert.True(hasher.Verify(replacementPassword, (await db.Users.AsNoTracking().SingleAsync(x => x.Id == targetUserId)).PasswordHash));
            Assert.Contains(targetUserId, revoked.Users);
            Assert.True((await admin.Grants.SaveAsync(storeId, adminId, new SaveRolePermissionsRequest
            {
                RoleId = hrRoleId, AdministratorPassword = administratorPassword, Reason = "Thu hồi quyền thử nghiệm"
            })).Success);
            Assert.Contains(hrId, revoked.Users);
        }
        await using (var db = fixture.CreateTenantContext(storeId))
        {
            var host = Services(db, storeId, hostId);
            var original = await db.RolePermissions.Where(x => x.RoleId == adminRoleId).Select(x => x.PermissionId).OrderBy(x => x).ToListAsync();
            await Assert.ThrowsAsync<ValidationAppException>(() => host.Grants.SaveAsync(storeId, hostId, new SaveRolePermissionsRequest
            {
                RoleId = adminRoleId, AdministratorPassword = hostPassword, Reason = "Thử gỡ quyền quản trị thiết yếu"
            }));
            Assert.Equal(original, await db.RolePermissions.Where(x => x.RoleId == adminRoleId).Select(x => x.PermissionId).OrderBy(x => x).ToListAsync());
            // Set up the last-admin edge only in the disposable copy.
            var oldAdmins = await db.UserInStores.Where(x => x.RoleId == adminRoleId && x.UserId != adminId).ToListAsync();
            foreach (var old in oldAdmins) old.IsActive = false;
            var second = NewUser(employeePassword);
            db.UserInStores.Add(new UserInStore { StoreId = storeId, User = second, RoleId = adminRoleId });
            await db.SaveChangesAsync();
        }
        int[] adminMappings;
        await using (var db = fixture.CreateTenantContext(storeId))
            adminMappings = await db.UserInStores.Where(x => x.RoleId == adminRoleId && x.IsActive && !x.IsDeleted).Select(x => x.Id).ToArrayAsync();
        Assert.Equal(2, adminMappings.Length);
        var results = await Task.WhenAll(adminMappings.Select(Lock));
        Assert.Single(results, x => x); Assert.Single(results, x => !x);
        await using (var db = fixture.CreateTenantContext(storeId))
        {
            Assert.Single(await db.UserInStores.Where(x => x.RoleId == adminRoleId && x.IsActive && !x.IsDeleted).ToListAsync());
            Assert.Equal(counts["AdminMenuItems"], await db.AdminMenuItems.LongCountAsync());
            var audits = await db.AuditLogs.Where(x => x.ActorUserId == adminId || x.ActorUserId == hostId || x.ActorUserId == hrId).ToListAsync();
            Assert.NotEmpty(audits);
            foreach (var audit in audits)
            {
                var text = audit.Summary + audit.OldValuesJson + audit.NewValuesJson;
                foreach (var secret in new[] { administratorPassword, hostPassword, employeePassword, replacementPassword, "PasswordHash", "AdministratorPassword" })
                    Assert.DoesNotContain(secret, text);
            }
        }
        output.WriteLine("Existing GaoAppDb store and role/permission identifiers: employee/ADMIN boundaries, reauthentication, HR edit, reset, grant revocation, audit, menu protection and concurrent last-admin checks passed.");

        User NewUser(string password) => new() { UserName = "standard_actor_" + Guid.NewGuid().ToString("N"), FullName = "Acceptance actor", PasswordHash = hasher.Hash(password) };
        async Task<bool> Lock(int id)
        {
            await using var db = fixture.CreateTenantContext(storeId);
            try { return (await Services(db, storeId, hostId).Users.ToggleActiveAsync(storeId, id, hostId, confirmation: Proof(null))).Success; }
            catch (ValidationAppException exception) { Assert.Contains("ADMIN", exception.Message); return false; }
        }
    }

    private static UpdateUserInStoreRequest Edit(int id, int role) => new() { Id = id, RoleId = role, IsActive = true };
    private static SecurityActionConfirmation Proof(string? password) => new() { AdministratorPassword = password, Reason = "Kiểm thử bảo mật trên dữ liệu chuẩn" };
    private sealed class Revocations : IAccountSessionRevoker
    {
        public HashSet<int> Users { get; } = [];
        public void RevokeUser(int userId) => Users.Add(userId);
    }
    private sealed class TestActor(int id) : ICurrentUser
    {
        public int? UserId => id;
        public string UserName => "standard-data-acceptance";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
    private static (SecurityAdministrationGuard Guard, UserInStoreAdminService Users, RolePermissionAdminService Grants, AdminMenuService Menus)
        Services(AppDbContext db, int storeId, int actorId, Revocations? revocations = null)
    {
        var tenant = new TenantContext(); tenant.SetStore(storeId, "standard-data");
        var hasher = new PasswordHasher();
        var guard = new SecurityAdministrationGuard(db, tenant, new TestActor(actorId), hasher, new HttpContextAccessor(), [revocations ?? new Revocations()]);
        var members = new UserInStoreRepository(db); var roles = new RoleRepository(db); var users = new UserRepository(db); var uow = new AppUnitOfWork(db);
        return (guard, new UserInStoreAdminService(members, roles, users, uow, hasher, guard),
            new RolePermissionAdminService(roles, new RolePermissionRepository(db), new PermissionRepository(db), members, uow, guard),
            new AdminMenuService(new AdminMenuRepository(db), new AdminMenuPermissionRepository(db), null!, security: guard));
    }
}
