using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05HttpTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("/shop/orders/qr")][InlineData("/shop/orders/qr/image")]
    public async Task Real_public_QR_POST_and_PNG_require_actual_antiforgery_cookie_and_header(string route)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await Web05PublicProcess.StartAsync(p.Case); var before = await p.Case.Web.CountsAsync();
        using var response = await host.Http.PostAsJsonAsync(route, new { token = Web04Support.GuestToken(p.Placed) }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, await p.Case.Web.CountsAsync());
    }

    [Fact]
    public async Task Real_public_private_QR_JSON_and_PNG_are_POST_only_no_store_and_never_expose_capability_or_credentials()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var token = Web04Support.GuestToken(p.Placed); var logs = new List<string>();
        await using (var host = await Web05PublicProcess.StartAsync(p.Case))
        {
            logs.Add(host.LogPath); await host.CsrfAsync(); var before = await p.Case.Web.CountsAsync();
            using (var json = await host.Http.PostAsJsonAsync("/shop/orders/qr", new { token }))
            {
                Assert.Equal(HttpStatusCode.OK, json.StatusCode); Assert.True(json.Headers.CacheControl?.NoStore); var text = await json.Content.ReadAsStringAsync();
                using var body = JsonDocument.Parse(text); Assert.Equal((int)StorefrontQrState.Pending, body.RootElement.GetProperty("state").GetInt32()); Assert.Equal("45000", body.RootElement.GetProperty("amountValue").GetString());
                foreach (var secret in new[] { token, "synthetic-client-secret", "synthetic-callback-key", "clientSecret", "bindingSnapshot", "registrationEvidence", "receivingLegalEntityId" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
            }
            using (var image = await host.Http.PostAsJsonAsync("/shop/orders/qr/image", new { token }))
            { Assert.Equal(HttpStatusCode.OK, image.StatusCode); Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType); Assert.True(image.Headers.CacheControl?.NoStore); Assert.Equal(Convert.FromBase64String(Web05BankTransport.ImageData.Split(',')[1]), await image.Content.ReadAsByteArrayAsync()); }
            using (var get = await host.Http.GetAsync("/shop/orders/qr")) Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
            Assert.Equal(before, await p.Case.Web.CountsAsync());
        }
        foreach (var log in logs) Assert.DoesNotContain(token, await File.ReadAllTextAsync(log));
    }

    [Fact]
    public async Task Real_private_QR_rejects_foreign_store_and_code_or_phone_lookup_with_generic_404()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var foreign = await fixture.Web05Async(); await using var host = await Web05PublicProcess.StartAsync(foreign); await host.CsrfAsync();
        foreach (var token in new[] { Web04Support.GuestToken(p.Placed), p.Placed.Code, p.Case.Request().RecipientPhone, new string('a', 43) })
        {
            using var response = await host.Http.PostAsJsonAsync("/shop/orders/qr", new { token }); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(p.Placed.Code, await response.Content.ReadAsStringAsync()); using var image = await host.Http.PostAsJsonAsync("/shop/orders/qr/image", new { token }); Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        }
    }

    [Theory]
    [InlineData("Production", true)][InlineData("Development", false)]
    public async Task Production_and_nonpreview_host_remain_closed_for_QR_page_commands_and_private_image(string environment, bool preview)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var host = await Web05PublicProcess.StartAsync(p.Case, environment, preview);
        using (var page = await host.Http.GetAsync("/shop/orders")) Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        foreach (var route in new[] { "/shop/orders/qr", "/shop/orders/qr/image" })
        { using var response = await host.Http.PostAsJsonAsync(route, new { token = Web04Support.GuestToken(p.Placed) }); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }

    [Fact]
    public async Task Without_operator_attestation_actual_checkout_does_not_offer_or_accept_QR_but_explicit_COD_remains_possible()
    {
        var c = await fixture.Web05Async(); await using var host = await Web05PublicProcess.StartAsync(c, registration: false); await host.CsrfAsync();
        var html = await host.Http.GetStringAsync("/shop/checkout"); Assert.DoesNotContain("<option value=\"1\"", html);
        using var qr = await host.Http.PostAsJsonAsync("/shop/checkout/quote", c.Request()); Assert.Equal(HttpStatusCode.UnprocessableEntity, qr.StatusCode);
        var cod = c.Request(); cod.PaymentMethod = GaoApp.Domain.Storefront.StorefrontPaymentMethod.Cod; using var accepted = await host.Http.PostAsJsonAsync("/shop/checkout/quote", cod); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await using var db = c.Context(); Assert.Empty(await db.StorefrontOrders.ToListAsync());
    }

    [Fact]
    public async Task Place_body_cannot_replace_the_quoted_QR_method_amount_account_store_or_provider_reference()
    {
        var c = await fixture.Web05Async(); var quote = await c.QuoteAsync(); await using var host = await Web05PublicProcess.StartAsync(c); await host.CsrfAsync(); var request = Web04Case.PlaceRequest(quote);
        using var response = await host.Http.PostAsJsonAsync("/shop/checkout/place", new { request.ClientRequestId, request.QuoteToken, request.Fingerprint,
            PaymentMethod = 0, AmountDue = 1, StoreId = int.MaxValue, BankAccountId = int.MaxValue, ProviderReference = "GA0000000001", ReceivingLegalEntityId = int.MaxValue }); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync(); var intent = await db.Set<GaoApp.Domain.Storefront.StorefrontPaymentIntent>().SingleAsync();
        Assert.Equal(GaoApp.Domain.Storefront.StorefrontPaymentMethod.Qr, order.PaymentMethod); Assert.Equal(45_000m, order.TotalDue); Assert.Equal(45_000m, intent.AmountDue);
        Assert.Equal(c.StoreId, intent.StoreId); Assert.Equal(c.Seed.BankAccountId, intent.BankAccountId); Assert.Equal(c.Seed.LegalEntityId, intent.ReceivingLegalEntityId); Assert.Matches("^W[0-9]{12}$", intent.ProviderReference);
    }
}
