using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04HttpTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("/shop/checkout/quote")][InlineData("/shop/checkout/place")][InlineData("/shop/orders/access")]
    public async Task Public_checkout_and_private_lookup_POSTs_require_actual_antiforgery_cookie_and_header(string endpoint)
    {
        var c = await fixture.Web04Async(); await using var host = await Web04PublicProcess.StartAsync(c); var before = await c.CountsAsync();
        using var response = await host.Http.PostAsJsonAsync(endpoint, new { token = new string('a', 43), clientRequestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Actual_quote_ignores_posted_store_price_totals_labels_zone_and_never_exposes_source_cost_or_private_identifiers()
    {
        var c = await fixture.Web04Async(); var foreign = await fixture.Web04Async(); await using var host = await Web04PublicProcess.StartAsync(c); await Csrf(host);
        var input = c.Request(); var before = await c.CountsAsync();
        using var response = await host.Http.PostAsJsonAsync("/shop/checkout/quote?StoreId=" + foreign.StoreId, new { input.Items, input.RecipientName, input.RecipientPhone, input.ProvinceCode, input.WardCode,
            input.StreetAddress, input.Note, StoreId = foreign.StoreId, GoodsAmount = 0, ShippingCharge = 0, TotalDue = 0, UnitPrice = 0, ShippingZoneId = 999999,
            ProvinceName = "FORGEDLABEL", WardName = "FORGEDLABEL", PaymentMethod = GaoApp.Domain.Storefront.StorefrontPaymentMethod.Cod, CustomerPriceTier = "WHOLESALE" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var text = await response.Content.ReadAsStringAsync(); var quote = JsonSerializer.Deserialize<StorefrontQuoteDto>(text, JsonOptions)!;
        Assert.Equal(25000, quote.GoodsAmount); Assert.Equal(20000, quote.ShippingCharge); Assert.Equal(45000, quote.TotalDue); Assert.Equal("Tỉnh thử nghiệm A", quote.Recipient.ProvinceName);
        foreach (var secret in new[] { "FORGEDLABEL", "costPrice", "warehouseId", "sourceLegalEntityId", "guestTokenHash", "connectionString", "bankAccount" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.CacheControl?.NoStore); Assert.Equal(before, await c.CountsAsync()); await using var other = foreign.Context(); Assert.Empty(await other.StorefrontQuotes.ToListAsync());
    }
    [Fact]
    public async Task Actual_checkout_returns_field_errors_for_recipient_and_address_membership_and_directory_endpoint_returns_labels()
    {
        var c = await fixture.Web04Async(); await using var host = await Web04PublicProcess.StartAsync(c); await Csrf(host); var request = c.Request(); request.RecipientName = ""; request.RecipientPhone = "abc"; request.WardCode = "02001";
        using var response = await host.Http.PostAsJsonAsync("/shop/checkout/quote", request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var fields = errors.RootElement.GetProperty("errors");
        Assert.True(fields.TryGetProperty("RecipientName", out _)); Assert.True(fields.TryGetProperty("RecipientPhone", out _)); Assert.True(fields.TryGetProperty("WardCode", out _));
        using var wards = await host.Http.GetAsync("/shop/checkout/wards?provinceCode=01"); Assert.Equal(HttpStatusCode.OK, wards.StatusCode); var text = await wards.Content.ReadAsStringAsync();
        using var directory = JsonDocument.Parse(text); Assert.Equal(2, directory.RootElement.GetProperty("wards").GetArrayLength()); Assert.Equal("Phường thử nghiệm A1", directory.RootElement.GetProperty("wards")[0].GetProperty("name").GetString());
        using var invalid = await host.Http.GetAsync("/shop/checkout/wards?provinceCode=UNKNOWN"); Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        await using var db = c.Context(); Assert.Empty(await db.StorefrontOrders.ToListAsync());
    }
    [Fact]
    public async Task Separate_process_restart_replays_same_committed_request_and_private_tracking_from_persistent_keyring()
    {
        var c = await fixture.Web04Async(); StorefrontPlaceOrderRequest request; StorefrontPlacedOrder original; int port; var logs = new List<string>();
        await using (var host = await Web04PublicProcess.StartAsync(c))
        {
            logs.Add(host.LogPath); port = new Uri(host.BaseUrl).Port; await Csrf(host); var quote = await Quote(host, c.Request()); request = Web04Case.PlaceRequest(quote);
            using var first = await host.Http.PostAsJsonAsync("/shop/checkout/place", request); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var result = await first.Content.ReadFromJsonAsync<StorefrontPlacementResult>(JsonOptions); Assert.Equal(StorefrontPlacementStatus.Placed, result!.Status); original = result.Order!;
        }
        var before = await c.CountsAsync(); await using (var restarted = await Web04PublicProcess.StartAsync(c, fixedPort: port))
        {
            logs.Add(restarted.LogPath); await Csrf(restarted); using var replay = await restarted.Http.PostAsJsonAsync("/shop/checkout/place", request); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(original, (await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>(JsonOptions))!.Order);
            using (var otherKey = await restarted.Http.PostAsJsonAsync("/shop/checkout/place", new StorefrontPlaceOrderRequest { ClientRequestId = Guid.NewGuid(), QuoteToken = request.QuoteToken, Fingerprint = request.Fingerprint }))
            { Assert.Equal(HttpStatusCode.Conflict, otherKey.StatusCode); Assert.DoesNotContain(Web04Support.GuestToken(original), await otherKey.Content.ReadAsStringAsync()); }
            var token = Web04Support.GuestToken(original); using var tracked = await restarted.Http.PostAsJsonAsync("/shop/orders/access", new { token }); Assert.Equal(HttpStatusCode.OK, tracked.StatusCode);
            var order = await tracked.Content.ReadFromJsonAsync<StorefrontGuestOrder>(JsonOptions); Assert.Equal(original.Code, order!.Code); Assert.Equal(original.TotalDue, order.TotalDue);
            Assert.DoesNotContain(token, await tracked.Content.ReadAsStringAsync()); Assert.True(tracked.Headers.CacheControl?.NoStore);
            using var code = await restarted.Http.PostAsJsonAsync("/shop/orders/access", new { token = original.Code, phone = c.Request().RecipientPhone }); Assert.Equal(HttpStatusCode.NotFound, code.StatusCode);
            var nonexistent = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            using var path = await restarted.Http.GetAsync("/shop/orders/" + nonexistent); Assert.Equal(HttpStatusCode.NotFound, path.StatusCode);
        }
        var foreign = await fixture.Web04Async(); await using (var foreignHost = await Web04PublicProcess.StartAsync(foreign))
        {
            logs.Add(foreignHost.LogPath); await Csrf(foreignHost); using var denied = await foreignHost.Http.PostAsJsonAsync("/shop/orders/access", new { token = Web04Support.GuestToken(original) }); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.DoesNotContain(original.Code, await denied.Content.ReadAsStringAsync());
        }
        foreach (var log in logs) Assert.DoesNotContain(Web04Support.GuestToken(original), await File.ReadAllTextAsync(log));
        Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Received_order_tracking_and_replay_remain_available_in_guarded_preview_during_sales_and_address_maintenance()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var placed = await c.PlaceAsync(request); var token = Web04Support.GuestToken(placed.Order!);
        await using (var db = c.Context()) { await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsEnabled, false)); await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false)); }
        File.Delete(c.AddressFile); var before = await c.CountsAsync(); await using var host = await Web04PublicProcess.StartAsync(c); await Csrf(host);
        using (var unavailable = await host.Http.GetAsync("/shop/checkout"))
        {
            Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode); var html = await unavailable.Content.ReadAsStringAsync();
            var notice = System.Text.RegularExpressions.Regex.Match(html, "<section class=\"empty-state error-state\">.*?<p>(.*?)</p>", System.Text.RegularExpressions.RegexOptions.Singleline);
            Assert.True(notice.Success); Assert.DoesNotContain("COD", WebUtility.HtmlDecode(notice.Groups[1].Value), StringComparison.OrdinalIgnoreCase); Assert.Contains("liên kết riêng", html);
        }
        using (var tracked = await host.Http.PostAsJsonAsync("/shop/orders/access", new { token })) { Assert.Equal(HttpStatusCode.OK, tracked.StatusCode); Assert.Equal(placed.Order!.Code, (await tracked.Content.ReadFromJsonAsync<StorefrontGuestOrder>(JsonOptions))!.Code); }
        using (var replay = await host.Http.PostAsJsonAsync("/shop/checkout/place", request)) { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(placed.Order, (await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>(JsonOptions))!.Order); }
        using (var catalog = await host.Http.GetAsync("/shop/products")) Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        using (var quote = await host.Http.PostAsJsonAsync("/shop/checkout/quote", c.Request())) Assert.False(quote.IsSuccessStatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    [Theory]
    [InlineData("Production", true)][InlineData("Development", false)]
    public async Task Production_and_non_preview_cannot_quote_place_or_disclose_existing_guest_order(string environment, bool preview)
    {
        var c = await fixture.Web04Async(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); await using var host = await Web04PublicProcess.StartAsync(c, environment: environment, preview: preview);
        var before = await c.CountsAsync(); foreach (var route in new[] { "/shop/checkout", "/shop/orders", "/shop/checkout/wards?provinceCode=01" }) { using var response = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
        using var access = await host.Http.PostAsJsonAsync("/shop/orders/access", new { token = Web04Support.GuestToken(placed.Order!) }); Assert.Equal(HttpStatusCode.NotFound, access.StatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Forwarded_host_and_posted_scope_cannot_bypass_actual_host_mapping_for_checkout_or_guest_lookup()
    {
        var c = await fixture.Web04Async(); await using var host = await Web04PublicProcess.StartAsync(c); await Csrf(host); var before = await c.CountsAsync();
        host.Http.DefaultRequestHeaders.Host = "foreign.web04.test"; host.Http.DefaultRequestHeaders.Add("X-Forwarded-Host", "127.0.0.1"); host.Http.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        using var quote = await host.Http.PostAsJsonAsync("/shop/checkout/quote", c.Request()); Assert.Equal(HttpStatusCode.NotFound, quote.StatusCode);
        using var access = await host.Http.PostAsJsonAsync("/shop/orders/access", new { token = new string('a', 43) }); Assert.Equal(HttpStatusCode.NotFound, access.StatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    [Theory]
    [InlineData("quote", 30)][InlineData("place", 20)][InlineData("guest", 60)]
    public async Task Public_rate_limits_bound_invalid_quote_place_and_private_token_attempts_without_order_effects(string kind, int limit)
    {
        var c = await fixture.Web04Async(); await using var host = await Web04PublicProcess.StartAsync(c); await Csrf(host); var before = await c.CountsAsync();
        var endpoint = kind == "guest" ? "/shop/orders/access" : "/shop/checkout/" + kind;
        object payload = kind switch { "guest" => new { token = "not-a-token" }, "place" => new StorefrontPlaceOrderRequest { Fingerprint = new string('A', 64) }, _ => new StorefrontQuoteRequest { RecipientPhone = "bad" } };
        for (var i = 0; i < limit; i++) { using var response = await host.Http.PostAsJsonAsync(endpoint, payload); Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound); }
        using var blocked = await host.Http.PostAsJsonAsync(endpoint, payload); Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Missing_persistent_keyring_blocks_new_checkout_and_does_not_use_ambient_user_keys()
    {
        var c = await fixture.Web04Async(); var before = await c.CountsAsync(); await using var host = await Web04PublicProcess.StartAsync(c, keys: false); await Csrf(host);
        using var response = await host.Http.PostAsJsonAsync("/shop/checkout/quote", c.Request()); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal(before, await c.CountsAsync());
    }
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    internal static async Task Csrf(Web04PublicProcess host) => host.Http.DefaultRequestHeaders.Add("RequestVerificationToken", await host.CsrfAsync());
    internal static async Task<StorefrontQuoteDto> Quote(Web04PublicProcess host, StorefrontQuoteRequest request)
    { using var response = await host.Http.PostAsJsonAsync("/shop/checkout/quote", request); Assert.Equal(HttpStatusCode.OK, response.StatusCode); return (await response.Content.ReadFromJsonAsync<StorefrontQuoteDto>(JsonOptions))!; }
}
