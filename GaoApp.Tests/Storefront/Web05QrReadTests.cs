using System.Text.Json;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05QrReadTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Guest_reopen_reads_same_pending_intent_image_and_exact_total_without_creating_or_contacting_bank()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var token = Web04Support.GuestToken(p.Placed); var calls = p.Bank.Requests.Count; var counts = await p.Case.Web.CountsAsync();
        StorefrontQrDto? first;
        await using (var db = p.Case.Context())
        {
            var reader = new StorefrontQrService(db, p.Case.Web.Clock); first = await reader.GetAsync(token);
            Assert.NotNull(first); Assert.Equal(StorefrontQrState.Pending, first.State); Assert.Equal(45_000m, first.Amount); Assert.Equal(p.Reference, first.ProviderReference); Assert.Equal(p.Reference, first.Content);
            Assert.True(first.QrImageReady); Assert.Equal(DateTimeKind.Utc, first.ExpiresAtUtc.Kind);
            Assert.Equal(Convert.FromBase64String(Web05BankTransport.ImageData.Split(',')[1]), await reader.ImageAsync(token));
            var json = JsonSerializer.Serialize(first); foreach (var secret in new[] { "ClientSecret", "CallbackApiKey", "BindingSnapshot", "ReceivingLegalEntity", "RegistrationEvidence", "synthetic-client-secret", token }) Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        }
        await using (var reopened = p.Case.Context()) Assert.Equal(first, await new StorefrontQrService(reopened, p.Case.Web.Clock).GetAsync(token));
        Assert.Equal(counts, await p.Case.Web.CountsAsync()); Assert.Equal(calls, p.Bank.Requests.Count);
    }

    [Theory]
    [InlineData("")][InlineData("12345")][InlineData("not-a-guest-capability")][InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Malformed_or_nonexistent_private_capability_does_not_reveal_QR_or_image(string token)
    {
        var p = await Web05Prepared.CreateAsync(fixture); await using var db = p.Case.Context(); var reader = new StorefrontQrService(db, p.Case.Web.Clock);
        Assert.Null(await reader.GetAsync(token)); Assert.Null(await reader.ImageAsync(token)); Assert.Null(await reader.GetAsync(p.Placed.Code));
    }

    [Fact]
    public async Task Foreign_store_and_inactive_store_cannot_read_another_private_QR()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var foreign = await fixture.Web05Async(); var token = Web04Support.GuestToken(p.Placed);
        await using (var db = foreign.Context()) { var reader = new StorefrontQrService(db, foreign.Web.Clock); Assert.Null(await reader.GetAsync(token)); Assert.Null(await reader.ImageAsync(token)); }
        await using (var db = p.Case.Context()) await db.Stores.Where(x => x.Id == p.Case.StoreId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false));
        await using var own = p.Case.Context(); Assert.Null(await new StorefrontQrService(own, p.Case.Web.Clock).GetAsync(token));
    }

    [Fact]
    public async Task Existing_QR_snapshot_remains_readable_during_sales_product_and_address_maintenance()
    {
        var p = await Web05Prepared.CreateAsync(fixture); var token = Web04Support.GuestToken(p.Placed);
        await using (var db = p.Case.Context())
        {
            await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsEnabled, false));
            await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(s => s.IsPublished, false));
        }
        File.Delete(p.Case.Web.AddressFile); await using var check = p.Case.Context(); var result = await new StorefrontQrService(check, p.Case.Web.Clock).GetAsync(token);
        Assert.NotNull(result); Assert.Equal(45_000m, result.Amount); Assert.Equal(p.Reference, result.ProviderReference); Assert.Equal(StorefrontQrState.Pending, result.State);
    }

    [Theory]
    [InlineData("paid")][InlineData("review")][InlineData("expired")]
    public async Task Paid_reviewed_or_expired_QR_cannot_continue_serving_a_payable_image(string state)
    {
        var p = await Web05Prepared.CreateAsync(fixture);
        if (state == "expired") p.Case.Web.Clock.Utc = p.Case.Web.Clock.Utc.AddMinutes(31);
        else { p.Bank.Pay(p.Reference, state == "paid" ? 45_000m : 1m); await p.Case.ProcessAsync(p.IntentId, p.Bank); }
        await using var db = p.Case.Context(); var reader = new StorefrontQrService(db, p.Case.Web.Clock); var token = Web04Support.GuestToken(p.Placed); var result = await reader.GetAsync(token);
        Assert.NotNull(result); Assert.Equal(state == "paid" ? StorefrontQrState.Paid : state == "review" ? StorefrontQrState.Review : StorefrontQrState.Expired, result.State);
        Assert.False(result.QrImageReady); Assert.Null(await reader.ImageAsync(token)); Assert.Equal(p.Reference, result.ProviderReference); Assert.Equal(45_000m, result.Amount);
    }
}
