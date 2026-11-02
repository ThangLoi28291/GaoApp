using System.Diagnostics;
using System.Net;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Identity;
using GaoApp.Infrastructure.Services.Audit;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class DurableAuditHttpSqlServerTests
{
    [Fact]
    public async Task Http_password_failure_rolls_back_and_persisted_events_are_delivered_after_a_real_test_host_restart()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var account = await app.AddAccountAsync(app.Stores[0], PermissionCodes.Catalog.Product.View);
        using var client = await app.LoginAsync(account);
        const string nextPassword = "Audit replacement password 2026!";
        await app.Database.ExecuteAsync("CREATE TRIGGER dbo.Issue10_HttpRejectEvidence ON dbo.AuditOutbox AFTER INSERT AS THROW 51015, 'synthetic-http-audit-error', 1;");
        using (var response = await client.Http.PostAsync("/admin/account/change-password", Password(account.Password, nextPassword)))
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using (var check = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var user = await check.Users.SingleAsync(x => x.Id == account.UserId);
            Assert.True(new PasswordHasher().Verify(account.Password, user.PasswordHash));
            Assert.False(await check.AuditLogs.AnyAsync(x => x.EntityName == nameof(User) && x.EntityId == account.UserId.ToString()));
        }
        await app.Database.ExecuteAsync("DROP TRIGGER dbo.Issue10_HttpRejectEvidence;");
        using (var response = await client.Http.PostAsync("/admin/account/change-password", Password(account.Password, nextPassword)))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Guid[] eventIds;
        await using (var check = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var pending = await check.AuditOutbox.Where(x => x.ProcessedAtUtc == null).ToListAsync();
            Assert.NotEmpty(pending);
            Assert.All(pending, x => { Assert.DoesNotContain(account.Password, x.PayloadJson); Assert.DoesNotContain(nextPassword, x.PayloadJson); });
            Assert.Contains(pending, x => AuditOutboxEvidence.Read(x).EntityName == nameof(User) &&
                AuditOutboxEvidence.Read(x).ChangedColumnsJson?.Contains("CredentialsChanged") == true);
            eventIds = pending.Select(x => x.EventId).ToArray();
            Assert.False(await check.AuditLogs.AnyAsync(x => x.EventId != null));
        }
        // Age only this fixture's queued records to exercise the operational backlog warning immediately.
        await app.Database.ExecuteAsync("UPDATE dbo.AuditOutbox SET CreatedAtUtc=DATEADD(MINUTE,-10,SYSUTCDATETIME()) WHERE ProcessedAtUtc IS NULL;");
        await app.RestartAsync(enableDeliveryOutbox: false, enableAuditOutbox: true);
        var wait = Stopwatch.StartNew();
        while (true)
        {
            await using var check = app.Database.CreateTenantContext(account.Store.StoreId);
            if (await check.AuditOutbox.CountAsync(x => eventIds.Contains(x.EventId) && x.ProcessedAtUtc != null) == eventIds.Length &&
                app.LogOutput.Contains("Audit outbox backlog")) break;
            Assert.True(wait.Elapsed < TimeSpan.FromSeconds(30), "The restarted audit worker did not drain persisted events: " + app.LogOutput);
            await Task.Delay(100);
        }
        await using (var check = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            Assert.Equal(eventIds.Length, await check.AuditLogs.CountAsync(x => x.EventId != null && eventIds.Contains(x.EventId.Value)));
            Assert.True(new PasswordHasher().Verify(nextPassword, (await check.Users.SingleAsync(x => x.Id == account.UserId)).PasswordHash));
            Assert.Equal(account.RoleId, (await check.UserInStores.SingleAsync(x => x.UserId == account.UserId)).RoleId);
        }
        await using (var other = app.Database.CreateTenantContext(app.Stores[1].StoreId))
            Assert.False(await other.AuditLogs.AnyAsync(x => x.EventId != null && eventIds.Contains(x.EventId.Value)));
        using var refreshed = await app.LoginAsync(account with { Password = nextPassword });
        Assert.DoesNotContain("Audit outbox worker unavailable", app.LogOutput);
        Assert.DoesNotContain(nextPassword, app.LogOutput);
        Assert.DoesNotContain(account.Password, app.LogOutput);
    }

    private static FormUrlEncodedContent Password(string current, string next)
        => new(new Dictionary<string, string> { ["CurrentPassword"] = current, ["NewPassword"] = next, ["ConfirmPassword"] = next });
}
