using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using GaoApp.Domain.Entities;
using GaoApp.Web.Configuration;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static ClaimsPrincipal RoutingUser(bool authenticated = true) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "7")], authenticated ? "Test" : null));
    private static AcbCallbackRoutingService Router(Fixture f) => new(f.Db, Options.Create(new AcbCallbackRoutingOptions { Hosts = ["www.gaomart.com.vn"] }));
    private static async Task AddRoutingAdmin(Fixture f, bool hostAdmin = true, bool active = true)
    {
        f.Db.Add(new User { Id = 7, UserName = "routing-admin-test", PasswordHash = "test-only", IsHostAdmin = hostAdmin, IsActive = active });
        await f.Db.SaveChangesAsync();
    }
    private static AcbCallbackRouteForm Selection(int? storeId, string? version = null) => new() { Host = "www.gaomart.com.vn", TargetStoreId = storeId, Version = version };

    [Fact]
    public async Task Store_selection_is_saved_by_id_survives_subdomain_change_and_keeps_audit_history()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f); await AddRoutingAdmin(f);
        var router = Router(f);
        var page = await router.PageAsync(RoutingUser(), default);
        Assert.Null(page.Routes.Single().TargetStoreId); // No automatic first-store selection.
        Assert.Equal(2, page.Stores.Count);
        await router.SaveAsync(RoutingUser(), Selection(1), default);
        var route = await f.Db.Set<AcbCallbackRoute>().SingleAsync();
        var originalStore = await f.Db.Stores.SingleAsync(x => x.Id == 1);
        originalStore.SubDomain = "future-name"; originalStore.SubDomainNormalized = "future-name";
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        page = await Router(f).PageAsync(RoutingUser(), default);
        Assert.Equal(1, page.Routes.Single().TargetStoreId);
        Assert.Equal("future-name", page.Stores.Single(x => x.Id == 1).Subdomain);
        Assert.Equal(["https://www.gaomart.com.vn/Admin/api-callback"], await router.UrlsForStoreAsync(1, default));
        Assert.Empty(await router.UrlsForStoreAsync(2, default));
        var history = Assert.Single(page.History);
        Assert.Null(history.PreviousStoreId); Assert.Equal(1, history.TargetStoreId); Assert.Equal(7, history.ActorUserId);
        await router.SaveAsync(RoutingUser(), Selection(1, page.Routes.Single().Version), default);
        Assert.Single(await f.Db.Set<AcbCallbackRouteChange>().ToListAsync()); // Saving the same selection does not fabricate a change.
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task Non_host_admin_inactive_or_unsigned_user_cannot_read_or_change_global_routing(bool hostAdmin, bool active, bool authenticated)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f); await AddRoutingAdmin(f, hostAdmin, active);
        f.Tenant.SetHostAdmin(); // Merely visiting the admin host is not authorization.
        var router = Router(f); var user = RoutingUser(authenticated);
        Assert.False(await router.CanManageAsync(user, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => router.PageAsync(user, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => router.SaveAsync(user, Selection(1), default));
        Assert.Empty(await f.Db.Set<AcbCallbackRoute>().ToListAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("no-key")]
    [InlineData("unconfigured-host")]
    public async Task Invalid_store_or_callback_configuration_is_rejected_without_saving(string scenario)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f); await AddRoutingAdmin(f);
        var form = Selection(1);
        if (scenario == "missing") form.TargetStoreId = 999;
        if (scenario == "inactive") (await f.Db.Stores.SingleAsync(x => x.Id == 1)).IsActive = false;
        if (scenario == "deleted") (await f.Db.Stores.SingleAsync(x => x.Id == 1)).IsDeleted = true;
        if (scenario == "no-key") (await f.Service.SettingsAsync(default))!.CallbackApiKeyProtected = "";
        if (scenario == "unconfigured-host") form.Host = "www.gaomart.com.vn.attacker.test";
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Router(f).SaveAsync(RoutingUser(), form, default));
        Assert.Empty(await f.Db.Set<AcbCallbackRoute>().ToListAsync());
        Assert.Empty(await f.Db.Set<AcbCallbackRouteChange>().ToListAsync());
    }

    [Fact]
    public async Task Stale_selection_cannot_overwrite_a_newer_route()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f); await AddRoutingAdmin(f);
        var router = Router(f);
        await router.SaveAsync(RoutingUser(), Selection(1), default);
        var route = await f.Db.Set<AcbCallbackRoute>().SingleAsync();
        route.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.SaveAsync(RoutingUser(), Selection(2, "stale"), default));
        Assert.Equal(1, route.TargetStoreId);
        Assert.Single(await f.Db.Set<AcbCallbackRouteChange>().ToListAsync());
    }

    [Fact]
    public async Task Pausing_keeps_history_and_cannot_bypass_outstanding_QR_guard_when_selecting_another_store()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f); await AddRoutingAdmin(f);
        var router = Router(f);
        await router.SaveAsync(RoutingUser(), Selection(1), default);
        var qr = await f.Service.TryCreateAsync(100, default);
        await router.SaveAsync(RoutingUser(), Selection(null), default);
        Assert.Null((await f.Db.Set<AcbCallbackRoute>().SingleAsync()).TargetStoreId);
        Assert.Empty(await router.UrlsForStoreAsync(1, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.SaveAsync(RoutingUser(), Selection(2), default));
        await router.SaveAsync(RoutingUser(), Selection(1), default); // Resuming the original store is safe.
        await f.Service.CancelAsync(qr!.Id, default);
        await router.SaveAsync(RoutingUser(), Selection(2), default);
        Assert.Equal(2, (await f.Db.Set<AcbCallbackRoute>().SingleAsync()).TargetStoreId);
        Assert.Equal(4, await f.Db.Set<AcbCallbackRouteChange>().CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_paused_selection_rejects_callback_without_falling_back_to_www(bool missing)
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        await using var app = await RoutingTestHost(f, target: null);
        if (missing) { f.Db.Remove(await f.Db.Set<AcbCallbackRoute>().SingleAsync()); await f.Db.SaveChangesAsync(); }
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        client.DefaultRequestHeaders.Add("x-api-key", "other-store-key");
        using var result = await client.PostAsJsonAsync("/Admin/api-callback", Notification("unknown"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.True(result.Headers.Contains("X-Acb-Diagnostic-Id"));
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().ToListAsync());
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("CALLBACK_STORE_NOT_SELECTED"));
    }

    [Fact]
    public async Task Callback_still_reaches_same_store_after_renaming_its_subdomain()
    {
        await using var f = await Fixture.Create();
        await AddRoutingStores(f);
        await using var app = await RoutingTestHost(f);
        var store = await f.Db.Stores.SingleAsync(x => x.Id == 1);
        store.SubDomain = "new-store-domain"; store.SubDomainNormalized = "new-store-domain"; await f.Db.SaveChangesAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        client.DefaultRequestHeaders.Add("x-api-key", "test-key");
        using var result = await client.PostAsJsonAsync("/Admin/api-callback", Notification("unknown"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(1, (await f.Db.Set<AcbCallbackReceipt>().IgnoreQueryFilters().SingleAsync()).StoreId);
        Assert.Equal("new-store-domain", f.Tenant.Subdomain);
    }
}
