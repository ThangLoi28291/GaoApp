using System.Security.Claims;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Services.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Security;

public sealed class EmployeeAccountConcurrencyTests
{
    [Fact]
    public async Task Two_requests_using_the_same_old_password_cannot_overwrite_each_other()
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seed = await fixture.SeedInventoryCatalogAsync();
        var password = "Original password 2026!";
        ClaimsPrincipal principal;
        int userId;
        await using (var db = fixture.CreateTenantContext(seed.StoreId))
        {
            var user = new User { UserName = "self-" + Guid.NewGuid().ToString("N"), PasswordHash = new PasswordHasher().Hash(password) };
            var role = new Role { StoreId = seed.StoreId, Code = "SELF_TEST", Name = "Nhân viên" };
            var member = new UserInStore { StoreId = seed.StoreId, User = user, Role = role };
            db.Add(member); await db.SaveChangesAsync(); userId = user.Id;
            principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("store_id", seed.StoreId.ToString()),
                new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(user, member))
            }, "test"));
        }
        var tenant = new TenantContext(); tenant.SetStore(seed.StoreId, "test");
        var barrier = new PasswordSaveBarrier();
        await using var firstDb = fixture.CreateTenantContext(seed.StoreId, barrier);
        await using var secondDb = fixture.CreateTenantContext(seed.StoreId, barrier);
        var first = new EmployeeAccountService(firstDb, tenant, new PasswordHasher());
        var second = new EmployeeAccountService(secondDb, tenant, new PasswordHasher());
        var results = await Task.WhenAll(
            first.ChangePasswordAsync(principal, password, "First replacement password!", default),
            second.ChangePasswordAsync(principal, password, "Second replacement password!", default));
        Assert.Single(results, x => x == PasswordChangeOutcome.Changed);
        Assert.Single(results, x => x == PasswordChangeOutcome.Conflict);
        await using var check = fixture.CreateTenantContext(seed.StoreId);
        var hash = (await check.Users.SingleAsync(x => x.Id == userId)).PasswordHash;
        Assert.True(new PasswordHasher().Verify(results[0] == PasswordChangeOutcome.Changed
            ? "First replacement password!" : "Second replacement password!", hash));
        var stale = new EmployeeAccountService(check, tenant, new PasswordHasher());
        Assert.Null(await stale.ProfileAsync(principal, default));
        Assert.Equal(PasswordChangeOutcome.InvalidSession,
            await stale.ChangePasswordAsync(principal, password, "Third replacement password!", default));
    }

    private sealed class PasswordSaveBarrier : SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) release.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }
}
