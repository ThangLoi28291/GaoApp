using System.Security.Claims;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Security;
using GaoApp.Infrastructure.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class SessionRevocationSqlServerTests
{
    [Fact]
    public async Task Real_sql_rowversions_accept_login_and_invalidate_password_or_membership_changes()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seed = await fixture.SeedInventoryCatalogAsync();
        await using var db = fixture.CreateTenantContext(seed.StoreId);
        var user = new User { UserName = $"session-{Guid.NewGuid():N}", PasswordHash = "synthetic-before" };
        await SecuritySeedData.SeedDefaultRolesForStoreAsync(db, seed.StoreId);
        var role = await db.Roles.SingleAsync(x => x.Code == "ADMIN");
        Assert.True(await db.RolePermissions.AnyAsync(x => x.RoleId == role.Id && x.Permission.Code == PermissionCodes.System.BankAccount.Manage));
        Assert.False(await db.RolePermissions.AnyAsync(x => x.Role.Code == "CASHIER" && x.Permission.Code == PermissionCodes.System.BankAccount.Manage));
        foreach (var roleCode in new[] { "ADMIN", "CASHIER" })
        foreach (var code in new[] { PermissionCodes.Pos.Order.Hold, PermissionCodes.Pos.Order.Discount })
            Assert.True(await db.RolePermissions.AnyAsync(x => x.Role.Code == roleCode && x.Permission.Code == code));
        var membership = new UserInStore { StoreId = seed.StoreId, User = user, Role = role };
        db.Add(membership); await db.SaveChangesAsync();
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "test");
        var validator = new SessionPrincipalValidator(db, tenant);
        var principal = Principal(AuthSessionStamp.Create(user, membership));
        Assert.True(await validator.ValidateAsync(principal));
        user.PasswordHash = "synthetic-after"; await db.SaveChangesAsync();
        Assert.False(await validator.ValidateAsync(principal));
        var refreshed = Principal(AuthSessionStamp.Create(user, membership));
        Assert.True(await validator.ValidateAsync(refreshed));
        membership.IsActive = false; await db.SaveChangesAsync();
        membership.IsActive = true; await db.SaveChangesAsync();
        Assert.False(await validator.ValidateAsync(refreshed));

        ClaimsPrincipal Principal(string stamp) => new(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("store_id", seed.StoreId.ToString()), new Claim("role_id", role.Id.ToString()),
            new Claim(ClaimTypes.Role, role.Code), new Claim(AuthSessionStamp.ClaimType, stamp)
        }, "test"));
    }
}
