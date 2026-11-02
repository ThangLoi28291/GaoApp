using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static AcbCallbackHealthService Health(Fixture f, params string[] hosts) => new(f.Db, f.Protocol,
        f.Diagnostics, Options.Create(new AcbCallbackRoutingOptions { Hosts = hosts.ToList() }));

    [Fact]
    public async Task Callback_health_shows_all_receipt_types_without_filtering_or_exposing_keys_or_changing_payments()
    {
        await using var f = await Fixture.Create();
        f.Db.Add(new AcbCallbackRoute { Host = "www.gaomart.com.vn", TargetStoreId = 1 });
        await f.Db.SaveChangesAsync();
        await f.Inbox.AcceptAsync(ListNotification("unknown-instant", code: "TRANSACTION_UPDATE"), default);
        await f.Inbox.AcceptAsync(ListNotification("unknown-daily"), default);
        var payments = f.Order.Payments.Count;
        var result = await Health(f, "www.gaomart.com.vn").ReadAsync(1,
            "https://chonthanh.gaomart.com.vn:8443/Admin/api-callback", false, default);
        Assert.Equal("Ready", result.KeyState);
        Assert.True(result.Enabled);
        Assert.Equal(2, result.PendingCount);
        Assert.Equal(2, result.Receipts.Count);
        Assert.NotNull(result.LastReceivedAtUtc);
        Assert.Equal("CurrentStore", Assert.Single(result.Routes).State);
        Assert.DoesNotContain("test-key", JsonSerializer.Serialize(result));
        Assert.DoesNotContain("test-secret", JsonSerializer.Serialize(result));
        Assert.Equal(payments, f.Order.Payments.Count);
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(0, f.FinalizeCount);
        f.Tenant.SetStore(2, "other");
        var other = await Health(f, "www.gaomart.com.vn").ReadAsync(2, "https://other/Admin/api-callback", false, default);
        Assert.Empty(other.Receipts);
        Assert.Equal("OtherStore", Assert.Single(other.Routes).State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Health(f).ReadAsync(1, "", false, default));
    }

    [Fact]
    public async Task Callback_health_reports_lost_key_ring_without_rendering_the_encrypted_key()
    {
        await using var f = await Fixture.Create();
        var settings = await f.Db.Set<StoreAcbSettings>().SingleAsync();
        settings.CallbackApiKeyProtected = "invalid-protected-key";
        await f.Db.SaveChangesAsync();
        var result = await Health(f).ReadAsync(1, "", false, default);
        Assert.Equal("Unreadable", result.KeyState);
        Assert.DoesNotContain(settings.CallbackApiKeyProtected, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Callback_health_distinguishes_unassigned_route_failed_auth_and_committed_receipt()
    {
        await using var f = await Fixture.Create();
        f.Diagnostics.Http("no-store", null, 503, "CALLBACK_STORE_NOT_SELECTED", null, 1, null, "missing", "www.gaomart.com.vn");
        f.Diagnostics.Http("wrong-key", 1, 403, "AUTH_REJECTED", null, 1, null, "x-api-key", "chonthanh.gaomart.com.vn");
        f.Diagnostics.Http("foreign", 2, 403, "AUTH_REJECTED", null, 1, null, "x-api-key", "other.gaomart.com.vn");
        f.Diagnostics.Http("foreign-route", null, 503, "CALLBACK_UNAVAILABLE", null, 1, null, "missing", "www.gaomart.com.vn", 2);
        f.Diagnostics.Http("browser-get", 1, 405, "METHOD_NOT_ALLOWED", null, 1, null, "missing", "chonthanh.gaomart.com.vn", method: "GET");
        var ordinary = await Health(f, "www.gaomart.com.vn").ReadAsync(1, "", false, default);
        Assert.Equal("wrong-key", Assert.Single(ordinary.HttpAttempts).DiagnosticId);
        var hostAdmin = await Health(f, "www.gaomart.com.vn").ReadAsync(1, "", true, default);
        Assert.Equal(2, hostAdmin.HttpAttempts.Count);
        Assert.Contains(hostAdmin.HttpAttempts, x => x.DiagnosticId == "no-store");
        Assert.Empty(hostAdmin.Receipts);
        Assert.Null(hostAdmin.LastReceivedAtUtc);
        Assert.Equal("NotSelected", Assert.Single(hostAdmin.Routes).State);
        f.Db.Add(new AcbCallbackRoute { Host = "www.gaomart.com.vn", TargetStoreId = 2 });
        await f.Db.SaveChangesAsync();
        Assert.Single((await Health(f, "www.gaomart.com.vn").ReadAsync(1, "", true, default)).HttpAttempts);
    }

    [Fact]
    public async Task Callback_diagnostics_are_bounded_and_never_treat_GET_or_failed_auth_as_receipts()
    {
        await using var f = await Fixture.Create();
        for (var i = 0; i < 250; i++)
            f.Diagnostics.Http(i.ToString(), 1, 403, "AUTH_REJECTED", null, 1, null, "x-api-key", "www.gaomart.com.vn");
        var attempts = f.Diagnostics.RecentForStore(1, []);
        Assert.Equal(20, attempts.Count);
        Assert.Equal("249", attempts[0].DiagnosticId);
        Assert.All(attempts, x => Assert.Null(x.ReceiptId));
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(0, f.FinalizeCount);
    }

    private sealed class CallbackFixedTime(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    [Fact]
    public async Task Reconciliation_defaults_to_today_in_Vietnam_and_includes_instant_and_daily_callbacks()
    {
        await using var f = await Fixture.Create();
        await f.Inbox.AcceptAsync(ListNotification("instant", code: "TRANSACTION_UPDATE"), default);
        await f.Inbox.AcceptAsync(ListNotification("daily"), default);
        var report = await new AcbReconciliationService(f.Db,
            new CallbackFixedTime(new DateTimeOffset(2026, 9, 7, 23, 30, 0, TimeSpan.Zero)))
            .ReadAsync(null, null, null, 1, default);
        Assert.Equal(ReportDay, report.From);
        Assert.Equal(ReportDay, report.To);
        Assert.Equal("ALL", report.Source);
        Assert.Equal(2, report.Total);
        Assert.Equal(2, report.Batches.Count);
        Assert.Equal(1, (await new AcbReconciliationService(f.Db).ReadAsync(ReportDay, ReportDay, "TRANSACTION_HISTORY", 1, default)).Total);
    }
}
