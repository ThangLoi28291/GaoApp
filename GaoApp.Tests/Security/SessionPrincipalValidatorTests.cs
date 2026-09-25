using System.Security.Claims;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using GaoApp.Web.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class SessionPrincipalValidatorTests
{
    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("password", false)]
    [InlineData("role-code", false)]
    [InlineData("membership-version", false)]
    [InlineData("disabled", false)]
    [InlineData("deleted-role", false)]
    [InlineData("legacy-cookie", false)]
    [InlineData("other-store", false)]
    [InlineData("host-domain", false)]
    [InlineData("forged-role", false)]
    public async Task Cookie_is_bound_to_current_credentials_membership_and_tenant(string change, bool expected)
    {
        var tenant = new TenantContext(); tenant.SetStore(7, "store7");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new InMemoryAppDbContext(options, tenant, new CurrentUser());
        var user = new User { UserName = "test", PasswordHash = "synthetic-hash", RowVersion = [1] };
        var role = new Role { StoreId = 7, Code = "ADMIN", Name = "Admin", RowVersion = [2] };
        var mapping = new UserInStore { StoreId = 7, User = user, Role = role, RowVersion = [3] };
        db.Add(mapping); await db.SaveChangesAsync();
        var identity = new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("store_id", "7"),
            new Claim("role_id", role.Id.ToString()), new Claim(ClaimTypes.Role, role.Code),
            new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(user, mapping))
        }, "test");
        switch (change)
        {
            case "password": user.PasswordHash = "replacement-hash"; break;
            case "role-code": role.Code = "CASHIER"; break;
            case "membership-version": mapping.RowVersion = [4]; break;
            case "disabled": user.IsActive = false; break;
            case "deleted-role": role.IsDeleted = true; break;
            case "legacy-cookie": identity.RemoveClaim(identity.FindFirst(AuthSessionStamp.ClaimType)!); break;
            case "other-store": tenant.SetStore(8, "store8"); break;
            case "host-domain": tenant.SetHostAdmin(); break;
            case "forged-role": identity.RemoveClaim(identity.FindFirst(ClaimTypes.Role)!); identity.AddClaim(new Claim(ClaimTypes.Role, "OTHER")); break;
        }
        // Save mutations with the original store context; tenant switching itself doesn't mutate data.
        if (change is not "other-store" and not "host-domain") await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(expected, await new SessionPrincipalValidator(db, tenant).ValidateAsync(new ClaimsPrincipal(identity)));
    }

    [Fact]
    public void Stamp_does_not_serialize_into_login_dto_and_old_hash_never_matches_new()
    {
        var user = new User { Id = 1, PasswordHash = "old-synthetic-hash" };
        var stamp = AuthSessionStamp.Create(user);
        Assert.DoesNotContain("old-synthetic-hash", stamp);
        user.PasswordHash = "new-synthetic-hash";
        Assert.False(AuthSessionStamp.Matches(stamp, AuthSessionStamp.Create(user)));
        var json = System.Text.Json.JsonSerializer.Serialize(new GaoApp.Application.DTOs.Auth.LoginResponse { SessionStamp = stamp });
        Assert.DoesNotContain(stamp, json);
    }

    [Fact]
    public async Task Removing_permission_takes_effect_without_waiting_for_a_cross_request_cache()
    {
        var tenant = new TenantContext(); tenant.SetStore(7, "store7");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new InMemoryAppDbContext(options, tenant, new CurrentUser());
        var user = new User { UserName = "test", PasswordHash = "synthetic-hash" };
        var role = new Role { StoreId = 7, Code = "MANAGER", Name = "Manager" };
        var membership = new UserInStore { StoreId = 7, User = user, Role = role };
        var permission = new Permission { Code = PermissionCodes.System.BankAccount.Manage, Name = "Manage bank", GroupName = "System" };
        var grant = new RolePermission { Role = role, Permission = permission };
        db.AddRange(membership, grant); await db.SaveChangesAsync();
        var service = new GaoApp.Application.Services.Security.CurrentStorePermissionService(
            new GaoApp.Infrastructure.Repositories.Security.UserInStoreRepository(db));
        Assert.True(await service.HasPermissionAsync(7, user.Id, permission.Code));
        db.Remove(grant); await db.SaveChangesAsync();
        Assert.False(await service.HasPermissionAsync(7, user.Id, permission.Code));
        Assert.False(await service.HasPermissionAsync(8, user.Id, permission.Code));
    }

    internal sealed class CurrentUser : ICurrentUser
    {
        public int? UserId => 1;
        public string? UserName => "test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
