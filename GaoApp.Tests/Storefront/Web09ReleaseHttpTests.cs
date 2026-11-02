using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09ReleaseHttpTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Real_pre_WEB07_schema_cannot_bootstrap_first_release_key_or_change_history_and_business_rows()
    {
        var old = new Web01Fixture(); Web09RecoverySupport.Plan(old.Database, "WEB09 intentional real pre-W07 schema fence, no monetary downgrade or fake migration history");
        try
        {
            await old.Database.MigrateAsync("20261101010000_AddDeliverySalePricePermission");
            var c = await Web09Case.CreateAsync(old); Assert.Empty(Directory.EnumerateFiles(c.Checkout.KeysDirectory, "*.xml"));
            string history; await using (var db = c.Context()) history = string.Join('|', await db.Database.GetAppliedMigrationsAsync());
            Assert.DoesNotContain("20261101120000_AddStorefrontCancellationRefunds", history); var before = await c.Checkout.CountsAsync();
            await using (var host = await Web09PublicProcess.StartAsync(c))
            {
                using var live = await host.Http.GetAsync("/health"); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
                using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
                Assert.Contains("CURRENT_SCHEMA_REQUIRED", await ready.Content.ReadAsStringAsync());
                using var catalog = await host.Http.GetAsync("/shop/products"); Assert.Equal(HttpStatusCode.ServiceUnavailable, catalog.StatusCode);
                Assert.Empty(Directory.EnumerateFiles(c.Checkout.KeysDirectory, "*.xml"));
            }
            await using var after = c.Context(); Assert.Equal(history, string.Join('|', await after.Database.GetAppliedMigrationsAsync()));
            Assert.Equal(before, await c.Checkout.CountsAsync()); Assert.Empty(Directory.EnumerateFiles(c.Checkout.KeysDirectory, "*.xml"));
        }
        finally { await old.DisposeAsync(); }
    }

    [Fact]
    public async Task Actual_malformed_mandatory_address_catalog_blocks_readiness_without_healthy_accepting_claim_or_business_effects()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); await File.WriteAllTextAsync(c.Checkout.AddressFile, "{WEB09-owned-malformed-address-fixture");
        var before = await c.Checkout.CountsAsync(); await using var host = await Web09PublicProcess.StartAsync(c);
        using var live = await host.Http.GetAsync("/health"); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        await AssertReadiness(ready, c, "release", false, false);
        using var document = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        var check = Assert.Single(document.RootElement.GetProperty("checks").EnumerateArray(), x => x.GetProperty("code").GetString() == "ADDRESS_CATALOG_UNAVAILABLE");
        Assert.False(check.GetProperty("passed").GetBoolean()); Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Production_server_opt_out_wins_over_genuine_database_activation_and_health_is_only_liveness()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var before = await c.Checkout.CountsAsync();
        await using var host = await Web09PublicProcess.StartAsync(c, release: false);
        using var live = await host.Http.GetAsync("/health"); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        await AssertReadiness(ready, c, "closed", false, false);
        using var home = await host.Http.GetAsync("/"); Assert.Equal(HttpStatusCode.ServiceUnavailable, home.StatusCode);
        using var catalog = await host.Http.GetAsync("/shop/products"); Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Genuine_Production_TLS_activation_readiness_and_pause_preserve_old_exact_HTTP_placement_replay_and_guest_access()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); await using var host = await Web09PublicProcess.StartAsync(c);
        using (var ready = await host.Http.GetAsync("/health/ready"))
        { Assert.Equal(HttpStatusCode.OK, ready.StatusCode); await AssertReadiness(ready, c, "release", true, true); }
        using (var products = await host.Http.GetAsync("/shop/products"))
        {
            Assert.Equal(HttpStatusCode.OK, products.StatusCode); var html = await products.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Chế độ xem trước", html); Assert.Contains("no-store", products.Headers.CacheControl!.ToString());
        }
        var csrf = await host.CsrfAsync("/shop/checkout");
        using var quoted = await Post(host, "/shop/checkout/quote", c.Checkout.Request(), csrf); Assert.Equal(HttpStatusCode.OK, quoted.StatusCode);
        var quote = await quoted.Content.ReadFromJsonAsync<StorefrontQuoteDto>(); Assert.NotNull(quote);
        var command = Web04Case.PlaceRequest(quote);
        using var placed = await Post(host, "/shop/checkout/place", command, csrf); Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var outcome = await placed.Content.ReadFromJsonAsync<StorefrontPlacementResult>(); Assert.NotNull(outcome?.Order); Assert.Equal(45000m, outcome.Order.TotalDue);
        var unused = Web04Case.PlaceRequest(await c.QuoteAsync()); await c.StopAsync(); var before = await c.Checkout.CountsAsync();
        using (var ready = await host.Http.GetAsync("/health/ready"))
        { Assert.Equal(HttpStatusCode.OK, ready.StatusCode); await AssertReadiness(ready, c, "release", true, false); }
        using (var denied = await Post(host, "/shop/checkout/quote", c.Checkout.Request(), csrf)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var denied = await Post(host, "/shop/checkout/place", unused, csrf)) Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.StatusCode);
        using (var replay = await Post(host, "/shop/checkout/place", command, csrf))
        {
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode); var actual = await replay.Content.ReadFromJsonAsync<StorefrontPlacementResult>(); Assert.NotNull(actual);
            Assert.Equal(StorefrontPlacementStatus.Placed, actual.Status); Assert.Equal(outcome.Order, actual.Order); Assert.Null(actual.ReplacementQuote);
            Assert.Equal("Đã nhận lại đúng đơn được tạo trước đó.", actual.Message);
            Assert.Equal("Cửa hàng đã nhận đơn và đang chờ chuẩn bị. Thanh toán COD khi nhận hàng.", outcome.Message);
        }
        var token = Web04Support.GuestToken(outcome.Order);
        using (var access = await Post(host, "/shop/orders/access", new { token }, csrf))
        { Assert.Equal(HttpStatusCode.OK, access.StatusCode); var guest = await access.Content.ReadFromJsonAsync<StorefrontGuestOrder>(); Assert.Equal(outcome.Order.Code, guest?.Code); }
        using (var forged = await Post(host, "/shop/checkout/place", new StorefrontPlaceOrderRequest { ClientRequestId = command.ClientRequestId,
            QuoteToken = command.QuoteToken, Fingerprint = new string('0', 64) }, csrf)) Assert.Equal(HttpStatusCode.Conflict, forged.StatusCode);
        Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("forwarded")]
    [InlineData("host")]
    [InlineData("tenant")]
    public async Task Production_admission_rejects_actual_HTTP_forwarded_spoof_wrong_host_and_nonexistent_tenant_without_mutation(string invalid)
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var before = await c.Checkout.CountsAsync();
        await using var host = await Web09PublicProcess.StartAsync(c, subdomain: invalid == "tenant" ? "foreign-does-not-exist-web09" : null);
        using var request = new HttpRequestMessage(HttpMethod.Get, (invalid is "plain" or "forwarded" ? host.PlainUrl : host.BaseUrl) + "/shop/products");
        if (invalid == "forwarded") { request.Headers.Add("X-Forwarded-Proto", "https"); request.Headers.Add("X-Forwarded-Host", "localhost"); request.Headers.Add("Forwarded", "proto=https;host=localhost"); }
        if (invalid == "host") request.Headers.Host = "foreign.example.invalid";
        using var denied = await host.Http.SendAsync(request); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Fact]
    public async Task Release_requires_protected_keys_and_missing_ring_with_existing_order_never_regenerates_a_key()
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); Assert.NotNull(placed.Order);
        var empty = Web09Support.DirectoryBefore(c.Directory, "missing-recovery-ring", "Deliberate NEW empty ring, no old key moved or deleted");
        var before = await c.Checkout.CountsAsync();
        await using (var host = await Web09PublicProcess.StartAsync(c, keyRoot: empty))
        {
            using var live = await host.Http.GetAsync("/health"); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            var text = await ready.Content.ReadAsStringAsync(); Assert.Contains("KEYRING_MISSING_PROOF", text);
            using var closed = await host.Http.GetAsync("/shop/products"); Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        }
        Assert.Empty(Directory.EnumerateFiles(empty, "*.xml", SearchOption.TopDirectoryOnly)); Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    [Theory]
    [InlineData("None")]
    [InlineData("invalid")]
    public async Task Unprotected_or_unknown_key_policy_cannot_release_even_when_server_and_store_flags_are_true(string keyProtection)
    {
        var c = await Web09Case.CreateAsync(fixture); await c.ActivateAsync(); var before = await c.Checkout.CountsAsync();
        await using var host = await Web09PublicProcess.StartAsync(c, keyProtection: keyProtection);
        using var ready = await host.Http.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        await AssertReadiness(ready, c, "release", false, false);
        Assert.Contains("KEY_PROTECTION_UNSUPPORTED", await ready.Content.ReadAsStringAsync());
        using var product = await host.Http.GetAsync("/shop/products"); Assert.Equal(HttpStatusCode.ServiceUnavailable, product.StatusCode);
        Assert.Equal(before, await c.Checkout.CountsAsync());
    }

    internal static async Task<HttpResponseMessage> Post(Web09PublicProcess host, string path, object body, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("RequestVerificationToken", csrf); return await host.Http.SendAsync(request);
    }
    private static async Task AssertReadiness(HttpResponseMessage response, Web09Case c, string mode, bool runtime, bool accepting)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString()); var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body); var value = document.RootElement;
        Assert.Equal(new[] { "acceptingNewOrders", "checks", "mode", "runtimeReady", "status" }, value.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(mode, value.GetProperty("mode").GetString()); Assert.Equal(runtime, value.GetProperty("runtimeReady").GetBoolean());
        Assert.Equal(accepting, value.GetProperty("acceptingNewOrders").GetBoolean()); Assert.Equal(runtime ? "ready" : "blocked", value.GetProperty("status").GetString());
        foreach (var check in value.GetProperty("checks").EnumerateArray())
        {
            Assert.Equal(new[] { "code", "passed" }, check.EnumerateObject().Select(x => x.Name).Order().ToArray());
            Assert.Matches("^[A-Z][A-Z0-9_]*$", check.GetProperty("code").GetString()!); Assert.Contains(check.GetProperty("passed").ValueKind, new[] { JsonValueKind.True, JsonValueKind.False });
        }
        Assert.DoesNotContain(c.Directory, body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog, body);
        Assert.DoesNotContain("synthetic-only", body); Assert.DoesNotContain("Data Source", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }
}
