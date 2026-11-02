using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Banking;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05TransportRecoveryTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Lost_provider_create_response_recovers_same_durable_reference_trace_and_amount_without_second_intent_or_charge_identity()
    {
        var c = await fixture.Web05Async(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); int id; string reference; string account;
        await using (var db = c.Context()) { var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync(); id = intent.Id; reference = intent.ProviderReference; account = (await db.StoreBankAccounts.SingleAsync()).AccountNumber; }
        var bank = new Web05BankTransport(account, c.Web.Clock) { DropFirstInitiateResponse = true }; await c.ProcessAsync(id, bank); Assert.Single(bank.Orders); Assert.Equal(1, bank.InitiateCalls);
        await using (var db = c.Context()) { Assert.Equal(StorefrontPaymentState.Pending, (await db.StorefrontOrders.SingleAsync()).PaymentState); Assert.Empty(await db.Set<StorefrontPaymentAllocation>().ToListAsync()); }
        await c.ProcessAsync(id, bank); await c.ProcessAsync(id, bank); await using var check = c.Context(); Assert.Single(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Single(await check.StorefrontOrders.ToListAsync());
        Assert.Single(bank.Orders); Assert.Equal(reference, bank.Orders.Single().Key); Assert.Equal(45_000m, bank.Orders.Single().Value.Amount);
        Assert.Equal((int)StorefrontQrState.Pending, (await check.Set<StorefrontQrProcessing>().SingleAsync()).State); Assert.NotNull((await check.Set<StorefrontQrProcessing>().SingleAsync()).QrPng);
        Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Equal(placed.Order!.Code, (await check.StorefrontOrders.SingleAsync()).Code);
    }

    [Theory]
    [InlineData("https://untrusted.invalid/bank-image.png")][InlineData("data:image/svg+xml,<svg onload=alert(1)></svg>")]
    [InlineData("data:image/png;base64,not-valid-base64")][InlineData("data:image/png;base64,bm90LWEtcG5n")]
    public async Task Provider_image_URL_or_non_PNG_data_is_not_followed_or_served_as_payable_QR(string image)
    {
        var c = await fixture.Web05Async(); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); int id; string account;
        await using (var db = c.Context()) { id = (await db.Set<StorefrontPaymentIntent>().SingleAsync()).Id; account = (await db.StoreBankAccounts.SingleAsync()).AccountNumber; }
        var bank = new Web05BankTransport(account, c.Web.Clock) { ImageOverride = image }; await c.ProcessAsync(id, bank);
        await using var check = c.Context(); var state = await check.Set<StorefrontQrProcessing>().SingleAsync(); Assert.Equal((int)StorefrontQrState.Review, state.State); Assert.Null(state.QrPng);
        var reader = new StorefrontQrService(check, c.Web.Clock); var token = Web04Support.GuestToken(placed.Order!); Assert.False((await reader.GetAsync(token))!.QrImageReady); Assert.Null(await reader.ImageAsync(token));
        Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Equal(1, bank.InitiateCalls);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Processor_with_missing_admin_keys_or_only_guest_keys_fails_before_any_bank_transport(bool guest)
    {
        var c = await fixture.Web05Async(); await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync())); await using var db = c.Context(); var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync();
        var rejectedTransport = new Web05RejectAllTransport(); var provider = guest ? c.Web.Protection() : Web05Support.AdminProtection(Path.Combine(Web05Support.RuntimePath("wrong-admin-ring"), "keys"));
        var protocol = new AcbProtocolCore(new HttpClient(rejectedTransport), provider); await new StorefrontBankProcessor(db, protocol, c.Web.Clock).ProcessAsync(intent.Id);
        Assert.Equal(0, rejectedTransport.Requests); await using var check = c.Context(); Assert.Equal(StorefrontPaymentState.Pending, (await check.StorefrontOrders.SingleAsync()).PaymentState);
        Assert.Empty(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()); Assert.Empty(await check.Set<StorefrontPaymentAllocation>().ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }
}
