using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb03"), Trait("Category", "StorefrontWeb03")]
public sealed class Web03HttpTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("Production", true)][InlineData("Production", false)][InlineData("Development", false)]
    public async Task Real_separate_host_stays_closed_without_every_preview_requirement(string environment, bool preview)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var host = await Web03PublicProcess.StartAsync(c, environment: environment, preview: preview);
        using var home = await host.Http.GetAsync("/shop"); Assert.Equal(HttpStatusCode.ServiceUnavailable, home.StatusCode); Assert.True(home.Headers.CacheControl?.NoStore);
        foreach (var route in new[] { "/shop/products", "/shop/cart", "/shop/media/1", "/admin", "/admin/pos" })
        { using var response = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }
    [Fact]
    public async Task Preview_cannot_bind_any_interface_even_when_peer_would_be_loopback()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Web03PublicProcess.StartAsync(c, bind: "0.0.0.0")); Assert.Contains("loopback", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("wrongHost")][InlineData("wrongSubdomain")][InlineData("settingsClosed")][InlineData("inactiveStore")]
    public async Task Host_mapping_settings_and_active_store_fail_closed(string reason)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using (var db = c.Context())
        {
            if (reason == "settingsClosed") await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
            if (reason == "inactiveStore") await db.Stores.Where(x => x.Id == c.StoreId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
        }
        await using var host = await Web03PublicProcess.StartAsync(c, host: reason == "wrongHost" ? "foreign.web03.test" : "127.0.0.1", subdomain: reason == "wrongSubdomain" ? "not-mapped" : null);
        using var response = await host.Http.GetAsync("/shop"); Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.ServiceUnavailable); Assert.DoesNotContain("product-card", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Public_pages_navigation_encoding_and_prices_use_real_current_store_without_login_or_writes()
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync();
        var request = c.Publication(baseUnit: false, pack: pack.Conversion); request.Description = "<script>web03-encoded</script>";
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, request);
        var before = await c.SideEffectsAsync(); await using var host = await Web03PublicProcess.StartAsync(c);
        foreach (var route in new[] { "/shop", "/shop/products", "/shop/cart", "/shop/orders", "/shop/checkout", "/shop/products/" + c.ProductId })
        {
            using var response = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            var html = await response.Content.ReadAsStringAsync();
            foreach (var nav in new[] { "/shop", "/shop/products", "/shop/cart", "/shop/orders" }) Assert.Contains("href=\"" + nav + "\"", html);
            Assert.DoesNotContain("admin/account/login", html); Assert.DoesNotContain("/admin/pos", html); Assert.DoesNotContain("CostPrice", html, StringComparison.OrdinalIgnoreCase);
            if (route.EndsWith("/" + c.ProductId)) { Assert.Contains("&lt;script&gt;web03-encoded&lt;/script&gt;", html); Assert.DoesNotContain("<script>web03-encoded", html); Assert.Contains(pack.Unit.ToString(), html); }
        }
        Assert.Equal(before, await c.SideEffectsAsync());
    }
    [Fact]
    public async Task Cart_HTTP_requires_CSRF_ignores_forged_store_and_prices_and_revalidates_hidden_source()
    {
        var c = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync();
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion));
        await using var host = await Web03PublicProcess.StartAsync(c);
        var payload = new { storeId = foreign.StoreId, unitPrice = 0, items = new[] { new { productId = c.ProductId, variantId = c.VariantId, unitId = pack.Unit, quantity = 1.2345m, unitPrice = 0, productName = "FORGED PRIVATE LABEL" } } };
        using (var denied = await host.Http.PostAsJsonAsync("/shop/cart/resolve", payload)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        host.Http.DefaultRequestHeaders.Add("RequestVerificationToken", await host.CsrfAsync());
        using (var response = await host.Http.PostAsJsonAsync("/shop/cart/resolve?StoreId=" + foreign.StoreId, payload))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(json.RootElement.GetProperty("canContinue").GetBoolean()); var line = json.RootElement.GetProperty("items")[0]; Assert.Equal(180000, line.GetProperty("unitPrice").GetDecimal());
            Assert.Equal(222210, json.RootElement.GetProperty("goodsEstimate").GetDecimal()); Assert.DoesNotContain("FORGED PRIVATE LABEL", json.RootElement.ToString());
        }
        await using (var db = c.Context()) await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false));
        using (var response = await host.Http.PostAsJsonAsync("/shop/cart/resolve", payload))
        { Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.False(json.RootElement.GetProperty("canContinue").GetBoolean()); Assert.False(json.RootElement.GetProperty("items")[0].GetProperty("available").GetBoolean()); }
    }
    [Theory]
    [InlineData("null")][InlineData("{\"items\":null}")][InlineData("{\"items\":[{\"productId\":1,\"variantId\":1,\"unitId\":1,\"quantity\":\"NaN\"}]}")]
    [InlineData("{\"items\":[{\"productId\":1,\"variantId\":1,\"unitId\":1,\"quantity\":1.23456}]}")]
    public async Task Invalid_JSON_or_quantity_returns_useful_error_without_orders(string json)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var host = await Web03PublicProcess.StartAsync(c);
        host.Http.DefaultRequestHeaders.Add("RequestVerificationToken", await host.CsrfAsync());
        using var response = await host.Http.PostAsync("/shop/cart/resolve", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.NotEmpty(await response.Content.ReadAsStringAsync()); await using var db = c.Context(); Assert.Empty(await db.StorefrontOrders.ToListAsync());
    }
    [Fact]
    public async Task Real_media_route_serves_only_current_same_store_public_links_and_never_raw_uploads_or_guessed_foreign_bytes()
    {
        var c = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync();
        const string ownPath = "uploads/products/web03-visible.png", privatePath = "uploads/products/web03-foreign.png", unlinkedPath = "uploads/products/web03-unlinked.png";
        var own = await c.AssetAsync(ownPath, "image/png"); var other = await foreign.AssetAsync(privatePath, "image/png"); var unlinked = await c.AssetAsync(unlinkedPath, "image/png");
        var secret = Web03Support.Png.Concat(Encoding.UTF8.GetBytes("FOREIGN_PRIVATE_WEB03_BYTES")).ToArray();
        var uploads = await c.UploadsAsync((own, ownPath, Web03Support.Png), (other, privatePath, secret), (unlinked, unlinkedPath, secret));
        await c.EnableSyntheticCatalogAsync(); var request = c.Publication(); request.Media = [new() { MediaAssetId = own, Kind = GaoApp.Domain.Storefront.StorefrontMediaKind.Image }];
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, request); await using var host = await Web03PublicProcess.StartAsync(c, uploads);
        using (var image = await host.Http.GetAsync("/shop/media/" + own))
        { Assert.Equal(HttpStatusCode.OK, image.StatusCode); Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType); Assert.Equal(Web03Support.Png, await image.Content.ReadAsByteArrayAsync()); Assert.True(image.Headers.CacheControl?.NoStore); Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single()); }
        foreach (var route in new[] { "/shop/media/" + other, "/shop/media/" + unlinked, "/" + privatePath, "/" + ownPath, "/shop/media/2147483647?path=" + privatePath })
        { using var denied = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("FOREIGN_PRIVATE_WEB03_BYTES", await denied.Content.ReadAsStringAsync()); }
        await using (var db = c.Context()) await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false));
        foreach (var route in new[] { "/shop/media/" + own, "/shop/products/" + c.ProductId })
        { using var denied = await host.Http.GetAsync(route); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); }
        using var list = await host.Http.GetAsync("/shop/products?Search=WEB02"); Assert.DoesNotContain("data-product-id=\"" + c.ProductId + "\"", await list.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("Page=0")][InlineData("PageSize=101")][InlineData("Sort=999")][InlineData("Sort=invalid")]
    public async Task Invalid_catalog_filters_render_encoded_error_page_with_navigation(string query)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var host = await Web03PublicProcess.StartAsync(c);
        using var response = await host.Http.GetAsync("/shop/products?" + query); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("/shop/products", await response.Content.ReadAsStringAsync());
    }
}
