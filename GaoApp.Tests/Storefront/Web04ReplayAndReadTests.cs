using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04ReplayAndReadTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Lost_response_replay_with_new_context_and_reopened_persistent_keys_returns_exact_same_order_and_private_link_once()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); StorefrontPlacementResult original;
        await using (var db = c.Context()) original = await c.Service(db, protection: c.Protection()).PlaceAsync(request);
        var before = await c.CountsAsync(); StorefrontPlacementResult replay;
        // New provider reloads the disk keyring; no in-memory outcome or service survives.
        await using (var db = c.Context()) replay = await c.Service(db, protection: c.Protection()).PlaceAsync(request);
        Assert.Equal(StorefrontPlacementStatus.Placed, replay.Status); Assert.Equal(original.Order, replay.Order); Assert.Equal(before, await c.CountsAsync());
        Assert.NotEmpty(System.IO.Directory.EnumerateFiles(c.KeysDirectory, "*.xml"));
        await using var verify = c.Context(); Assert.Single(await verify.StorefrontOrders.ToListAsync()); Assert.Single(await verify.Set<StorefrontCommandReceipt>().ToListAsync());
        Assert.Single(await verify.Set<StorefrontEvent>().ToListAsync()); Assert.Single(await verify.Set<StorefrontOutboxMessage>().ToListAsync());
    }
    [Fact]
    public async Task Concurrent_identical_place_requests_share_one_durable_outcome_and_one_reservation()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var results = await Task.WhenAll(c.PlaceAsync(request), c.PlaceAsync(request), c.PlaceAsync(request));
        Assert.All(results, x => Assert.Equal(StorefrontPlacementStatus.Placed, x.Status)); Assert.All(results, x => Assert.Equal(results[0].Order, x.Order));
        await using var db = c.Context(); Assert.Single(await db.StorefrontOrders.ToListAsync()); Assert.Single(await db.InventoryReservations.ToListAsync()); Assert.Equal(1, (await db.InventoryBalances.SingleAsync()).ReservedQty);
    }
    [Fact]
    public async Task Same_idempotency_key_with_different_quote_payload_is_conflict_and_does_not_duplicate_order()
    {
        var c = await fixture.Web04Async(); var first = Web04Case.PlaceRequest(await c.QuoteAsync()); await c.PlaceAsync(first); var second = Web04Case.PlaceRequest(await c.QuoteAsync(c.Request(2)), first.ClientRequestId);
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ConflictAppException>(() => c.PlaceAsync(second)); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Missing_protection_key_never_creates_a_new_order_when_replaying_an_already_committed_request()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); await c.PlaceAsync(request); var before = await c.CountsAsync();
        var wrongKeyDir = Path.Combine(c.Directory, "unrelated-keyring"); System.IO.Directory.CreateDirectory(wrongKeyDir);
        var unrelated = DataProtectionProvider.Create(new DirectoryInfo(wrongKeyDir), b => b.SetApplicationName("WEB04 unrelated app"));
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db, protection: unrelated).PlaceAsync(request)); Assert.Equal(before, await c.CountsAsync());
    }
    [Theory]
    [InlineData("emptyClient")][InlineData("emptyQuote")][InlineData("badFingerprint")][InlineData("wrongFingerprint")][InlineData("unknownQuote")][InlineData("foreignQuote")]
    public async Task Malformed_unknown_foreign_or_tampered_place_capabilities_do_not_create_side_effects(string invalid)
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync());
        switch (invalid)
        {
            case "emptyClient": request.ClientRequestId = Guid.Empty; break;
            case "emptyQuote": request.QuoteToken = Guid.Empty; break;
            case "badFingerprint": request.Fingerprint = new string('Z', 64); break;
            case "wrongFingerprint": request.Fingerprint = new string('A', 64); break;
            case "unknownQuote": request.QuoteToken = Guid.NewGuid(); break;
            case "foreignQuote": request = Web04Case.PlaceRequest(await (await fixture.Web04Async()).QuoteAsync()); break;
        }
        var before = await c.CountsAsync(); var exception = await Record.ExceptionAsync(() => c.PlaceAsync(request)); Assert.NotNull(exception);
        Assert.True(exception is ValidationAppException or ConflictAppException or NotFoundAppException, exception.ToString()); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Received_order_snapshot_and_durable_replay_survive_product_price_name_hide_settings_close_and_missing_address_catalog()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var placed = await c.PlaceAsync(request); var token = Web04Support.GuestToken(placed.Order!);
        string before; await using (var db = c.Context())
        {
            var snapshot = Assert.IsType<StorefrontGuestOrder>(await c.Service(db).GetGuestOrderAsync(token));
            Assert.Equal(DateTimeKind.Utc, snapshot.CreatedAtUtc.Kind); Assert.Equal(placed.Order!.CreatedAtUtc.Ticks, snapshot.CreatedAtUtc.Ticks);
            before = JsonSerializer.Serialize(snapshot);
        }
        await using (var db = c.Context())
        {
            await db.Products.ExecuteUpdateAsync(x => x.SetProperty(p => p.Name, "CHANGED AFTER RECEIPT").SetProperty(p => p.BasePrice, 7));
            await db.ProductUnitConversions.ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, 9));
            await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false)); await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsEnabled, false));
        }
        File.Delete(c.AddressFile); c.Clock.Utc = c.Clock.Utc.AddDays(100);
        await using (var db = c.Context()) Assert.Equal(before, JsonSerializer.Serialize(await c.Service(db).GetGuestOrderAsync(token)));
        Assert.Equal(placed.Order, (await c.PlaceAsync(request)).Order); await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        await using var inboxDb = c.Context(); var inbox = Inbox(c, inboxDb); var row = Assert.Single((await inbox.ListAsync()).Items);
        Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind); Assert.Equal(placed.Order!.CreatedAtUtc.Ticks, row.CreatedAtUtc.Ticks);
        Assert.Equal(before, JsonSerializer.Serialize(await inbox.GetAsync(row.Id))); Assert.Equal(1, (await inbox.NotificationsAsync(0)).NewOrders);
    }
    [Theory]
    [InlineData("")][InlineData("0901234567")][InlineData("W123456789")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")][InlineData("<script>")][InlineData("/shop/orders#token")]
    public async Task Guest_lookup_has_no_phone_code_or_malformed_token_enumeration(string token)
    {
        var c = await fixture.Web04Async(); await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); var before = await c.CountsAsync();
        await using var db = c.Context(); Assert.Null(await c.Service(db).GetGuestOrderAsync(token)); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Inactive_store_disables_guest_capability_even_when_its_hash_and_snapshot_exist()
    {
        var c = await fixture.Web04Async(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); var token = Web04Support.GuestToken(placed.Order!);
        await using var db = c.Context(); await db.Stores.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false)); Assert.Null(await c.Service(db).GetGuestOrderAsync(token));
    }
    [Fact]
    public async Task Guest_token_and_staff_order_id_are_bound_to_actual_current_tenant()
    {
        var a = await fixture.Web04Async(); var b = await fixture.Web04Async(); var placed = await a.PlaceAsync(Web04Case.PlaceRequest(await a.QuoteAsync())); var token = Web04Support.GuestToken(placed.Order!);
        int id; await using (var db = a.Context()) { id = (await db.StorefrontOrders.SingleAsync()).Id; Assert.NotNull(await a.Service(db).GetGuestOrderAsync(token)); }
        await using (var db = b.Context())
        {
            Assert.Null(await b.Service(db).GetGuestOrderAsync(token)); var inbox = Inbox(b, db); Assert.Empty((await inbox.ListAsync()).Items); await Assert.ThrowsAsync<NotFoundAppException>(() => inbox.GetAsync(id));
            Assert.Equal(0, (await inbox.NotificationsAsync(0)).NewOrders);
        }
    }
    [Theory]
    [InlineData("anonymous")][InlineData("noUser")][InlineData("noPermission")][InlineData("foreignPermission")][InlineData("inactiveStore")]
    public async Task Inbox_list_detail_and_notification_require_authenticated_same_store_effective_permission(string denied)
    {
        var c = await fixture.Web04Async(); await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); await using var db = c.Context(); var id = (await db.StorefrontOrders.SingleAsync()).Id;
        if (denied == "inactiveStore") await db.Stores.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
        var actor = new Web01Actor(denied == "noUser" ? null : c.Seed.UserId, denied != "anonymous");
        var permissions = new Web01Permissions(denied == "foreignPermission" ? c.StoreId + 100000 : c.StoreId, c.Seed.UserId,
            denied == "noPermission" ? [] : [StorefrontPermissions.ViewOrders]);
        var inbox = new StorefrontOrderInboxService(db, actor, permissions);
        await Assert.ThrowsAsync<ForbiddenAppException>(() => inbox.ListAsync()); await Assert.ThrowsAsync<ForbiddenAppException>(() => inbox.GetAsync(id));
        await Assert.ThrowsAsync<ForbiddenAppException>(() => inbox.NotificationsAsync(0));
    }
    [Fact]
    public async Task Inbox_new_order_notification_is_one_per_committed_order_and_pagination_is_stable()
    {
        var c = await fixture.Web04Async(); var a = Web04Case.PlaceRequest(await c.QuoteAsync()); await c.PlaceAsync(a); await c.PlaceAsync(a);
        int first; await using (var db = c.Context()) first = (await db.StorefrontOrders.SingleAsync()).Id;
        await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync(c.Request(2)))); await using var verify = c.Context(); var inbox = Inbox(c, verify);
        var page1 = await inbox.ListAsync(1, 1); var page2 = await inbox.ListAsync(2, 1); Assert.Equal(2, page1.TotalCount); Assert.Equal(2, page1.AwaitingPreparationCount);
        Assert.True(page1.Items.Single().Id > page2.Items.Single().Id); Assert.Equal(first, page2.Items.Single().Id);
        var notification = await inbox.NotificationsAsync(first); Assert.Equal(1, notification.NewOrders); Assert.Equal(page1.Items.Single().Id, notification.NewestId);
        Assert.Equal(0, (await inbox.NotificationsAsync(notification.NewestId)).NewOrders); Assert.Empty(await verify.AdminMenuItems.ToListAsync());
    }
    private static StorefrontOrderInboxService Inbox(Web04Case c, GaoApp.Infrastructure.Data.AppDbContext db)
        => new(db, new Web01Actor(c.Seed.UserId), new Web01Permissions(c.StoreId, c.Seed.UserId, [StorefrontPermissions.ViewOrders]));
}
