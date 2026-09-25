using System.Data.Common;
using System.Security.Claims;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Tests.Configuration;
using GaoApp.Web.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosRealtimeBatchSqlServerTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public async Task Live_validation_uses_three_reads_per_batch_and_isolates_a_revoked_user(int clientCount)
    {
        await using var fixture = new InventoryPostingLocalDb();
        await fixture.MigrateAsync();
        var seed = await fixture.SeedInventoryCatalogAsync();
        await using var db = fixture.CreateTenantContext(seed.StoreId);
        await SecuritySeedData.SeedDefaultRolesForStoreAsync(db, seed.StoreId);
        var role = await db.Roles.SingleAsync(x => x.Code == "ADMIN");
        var terminal = new POSTerminal { StoreId = seed.StoreId, Code = "BATCH", Name = "Synthetic terminal" };
        db.POSTerminals.Add(terminal);
        var members = Enumerable.Range(0, clientCount).Select(i => new UserInStore {
            StoreId = seed.StoreId, Role = role,
            User = new User { UserName = $"batch-{Guid.NewGuid():N}", FullName = "Synthetic user", PasswordHash = "synthetic" }
        }).ToArray();
        db.UserInStores.AddRange(members);
        await db.SaveChangesAsync();
        var sessions = members.Select((member, i) => new PosRealtimeSessionValidator.Session(i.ToString(),
            new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, member.UserId.ToString()), new Claim("store_id", seed.StoreId.ToString()),
                new Claim("terminal_id", terminal.Id.ToString()), new Claim("role_id", role.Id.ToString()),
                new Claim(ClaimTypes.Role, role.Code), new Claim(AuthSessionStamp.ClaimType, AuthSessionStamp.Create(member.User, member))
            }, "test")), member.UserId, seed.StoreId, terminal.Id)).ToArray();

        var counter = new ReadCounter();
        await using var provider = new ServiceCollection()
            .AddScoped<AppDbContext>(_ => fixture.CreateHostContext(counter)).BuildServiceProvider();
        var validator = new PosRealtimeSessionValidator(provider.GetRequiredService<IServiceScopeFactory>());
        var allowed = await validator.ValidateAsync(sessions, CancellationToken.None);
        Assert.Equal(clientCount, allowed.Count);
        Assert.All(allowed.Values, access => Assert.True(access.CanBroadcastPayment));
        Assert.Equal(3, counter.Count);

        members[0].User.IsActive = false;
        await db.SaveChangesAsync();
        counter.Count = 0;
        allowed = await validator.ValidateAsync(sessions, CancellationToken.None);
        Assert.Equal(clientCount - 1, allowed.Count);
        Assert.DoesNotContain(sessions[0].ConnectionId, allowed.Keys);
        Assert.Equal(3, counter.Count);
    }

    private sealed class ReadCounter : DbCommandInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }
}
