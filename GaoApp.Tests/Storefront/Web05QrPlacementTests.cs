using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05QrPlacementTests(Web01Fixture fixture)
{
    [Fact]
    public async Task QR_quote_and_placement_lock_goods_plus_shipping_and_queue_real_web_intent_without_POS_or_money_posting()
    {
        var c = await fixture.Web05Async(); var quote = await c.QuoteAsync();
        Assert.Equal(StorefrontPaymentMethod.Qr, quote.PaymentMethod); Assert.Equal(25_000m, quote.GoodsAmount);
        Assert.Equal(20_000m, quote.ShippingCharge); Assert.Equal(45_000m, quote.TotalDue);
        await using (var quoted = c.Context())
        {
            Assert.Empty(await quoted.StorefrontOrders.ToListAsync()); Assert.Empty(await quoted.InventoryReservations.ToListAsync());
            Assert.Empty(await quoted.Set<StorefrontPaymentIntent>().ToListAsync());
        }
        var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync(); var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync();
        Assert.Equal(StorefrontOrderPhase.AwaitingPayment, order.Phase); Assert.Equal(StorefrontPaymentState.Pending, order.PaymentState);
        Assert.Equal(StorefrontDeliveryState.NotPlanned, order.DeliveryState); Assert.Equal(StorefrontPaymentMethod.Qr, order.PaymentMethod);
        Assert.Equal(45_000m, order.TotalDue); Assert.Equal(order.TotalDue, intent.AmountDue); Assert.Equal(order.Id, intent.StorefrontOrderId);
        Assert.Equal(c.Seed.BankAccountId, intent.BankAccountId); Assert.Equal(c.Seed.LegalEntityId, intent.ReceivingLegalEntityId);
        Assert.Equal("WEB05-REGISTERED", intent.BankTerminalCode); Assert.Equal("WEB05-USER", intent.BankUserCode);
        Assert.Matches("^W[0-9]{12}$", intent.ProviderReference); Assert.Equal(13, intent.ProviderReference.Length);
        Assert.Equal(c.Web.Clock.Utc.AddMinutes(30), DateTime.SpecifyKind(intent.ExpiresAtUtc, DateTimeKind.Utc));
        var registry = await db.Set<BankPaymentReference>().SingleAsync(x => x.Reference == intent.ProviderReference);
        Assert.Equal(BankPaymentChannel.Storefront, registry.Channel); Assert.Equal(c.StoreId, registry.OriginStoreId); Assert.Equal(intent.Id, registry.StorefrontPaymentIntentId); Assert.Null(registry.PosQrRequestId);
        var processing = await db.Set<StorefrontQrProcessing>().SingleAsync(); Assert.Equal(intent.Id, processing.PaymentIntentId); Assert.Equal((int)StorefrontQrState.Queued, processing.State);
        Assert.Null(processing.QrPng); Assert.Single(await db.InventoryReservations.ToListAsync());
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSTerminals.ToListAsync());
        Assert.Empty(await db.PosPaymentQrRequests.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.InventoryTransactions.ToListAsync());
        Assert.Empty(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }

    [Theory]
    [InlineData("missing")][InlineData("unverified")][InlineData("merchant")][InlineData("bank-account")]
    [InlineData("receiving-owner")][InlineData("physical-account")][InlineData("terminal")][InlineData("user")]
    public async Task Missing_or_mismatched_trusted_registration_blocks_requested_QR_without_silent_COD_fallback(string change)
    {
        var c = await fixture.Web05Async(); var value = change switch
        {
            "missing" => null,
            "unverified" => c.Registration with { Verified = false },
            "merchant" => c.Registration with { MerchantId = "OTHER" },
            "bank-account" => c.Registration with { BankAccountId = int.MaxValue },
            "receiving-owner" => c.Registration with { ReceivingLegalEntityId = int.MaxValue },
            "physical-account" => c.Registration with { AccountIdentity = new string('F', 64) },
            "terminal" => c.Registration with { TerminalCode = "OTHER" },
            _ => c.Registration with { UserCode = "OTHER" }
        };
        await using var db = c.Context(); var service = c.Service(db, new Web05Registration(c.StoreId, value));
        await Assert.ThrowsAsync<ValidationAppException>(() => service.QuoteAsync(c.Request()));
        Assert.Empty(await db.StorefrontQuotes.ToListAsync()); Assert.Empty(await db.StorefrontOrders.ToListAsync()); Assert.Empty(await db.InventoryReservations.ToListAsync());
        Assert.Empty(await db.Set<StorefrontPaymentIntent>().ToListAsync());
    }

    [Fact]
    public async Task QR_never_changes_ineligible_SKU_invoice_flag_and_customer_can_explicitly_request_COD()
    {
        var c = await fixture.Web05Async(); await using (var db = c.Context()) await db.ProductVariants.Where(x => x.Id == c.Seed.VariantId).ExecuteUpdateAsync(x => x.SetProperty(v => v.HasInputInvoice, false));
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        var cod = c.Request(); cod.PaymentMethod = StorefrontPaymentMethod.Cod; var quote = await c.QuoteAsync(cod);
        Assert.Equal(StorefrontPaymentMethod.Cod, quote.PaymentMethod); Assert.Equal(45_000m, quote.TotalDue);
        await using var check = c.Context(); Assert.False((await check.ProductVariants.AsNoTracking().SingleAsync(x => x.Id == c.Seed.VariantId)).HasInputInvoice);
        Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.StorefrontOrders.ToListAsync());
    }

    [Theory]
    [InlineData("account")][InlineData("owner")][InlineData("collection-authority")][InlineData("bank-settings")]
    public async Task Broken_bank_or_collection_scope_cannot_quote_a_payable_web_QR(string change)
    {
        var c = await fixture.Web05Async(); await using (var db = c.Context())
        {
            if (change == "account") await db.StoreBankAccounts.Where(x => x.Id == c.Seed.BankAccountId).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsActive, false));
            else if (change == "owner") await db.LegalEntities.Where(x => x.Id == c.Seed.LegalEntityId).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsActive, false));
            else if (change == "collection-authority") await db.Set<StorefrontBankCollectionAuthority>().ExecuteUpdateAsync(x => x.SetProperty(a => a.IsActive, false));
            else await db.Set<StoreAcbSettings>().ExecuteUpdateAsync(x => x.SetProperty(a => a.Enabled, false));
        }
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        await using var check = c.Context(); Assert.Empty(await check.StorefrontOrders.ToListAsync()); Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.InventoryReservations.ToListAsync());
    }

    [Theory]
    [InlineData("2147483647", true)][InlineData("2147483648", false)]
    public async Task QR_int32_boundary_includes_shipping_and_never_truncates_or_splits_the_amount(string totalText, bool allowed)
    {
        var c = await fixture.Web05Async(); var total = decimal.Parse(totalText, System.Globalization.CultureInfo.InvariantCulture);
        await using (var db = c.Context()) await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, total - 20_000m));
        if (allowed)
        {
            var quote = await c.QuoteAsync(); Assert.Equal(total, quote.TotalDue); Assert.Equal(total - 20_000m, quote.GoodsAmount);
            var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
            await using var check = c.Context(); Assert.Equal(total, (await check.Set<StorefrontPaymentIntent>().SingleAsync()).AmountDue);
        }
        else
        {
            await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync()); var cod = c.Request(); cod.PaymentMethod = StorefrontPaymentMethod.Cod;
            Assert.Equal(total, (await c.QuoteAsync(cod)).TotalDue); await using var check = c.Context(); Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.StorefrontOrders.ToListAsync());
        }
    }

    [Fact]
    public async Task Replaying_committed_QR_placement_with_new_service_and_guest_key_provider_returns_one_order_intent_registry_and_hold()
    {
        var c = await fixture.Web05Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var first = await c.PlaceAsync(request); var counts = await c.Web.CountsAsync();
        var retry = await c.PlaceAsync(request); Assert.Equal(first.Status, retry.Status); Assert.Equal(first.Order, retry.Order); Assert.Equal(counts, await c.Web.CountsAsync());
        await using var db = c.Context(); Assert.Single(await db.StorefrontOrders.ToListAsync()); var intent = await db.Set<StorefrontPaymentIntent>().SingleAsync();
        Assert.Single(await db.Set<BankPaymentReference>().Where(x => x.StorefrontPaymentIntentId == intent.Id).ToListAsync()); Assert.Single(await db.Set<StorefrontQrProcessing>().ToListAsync()); Assert.Single(await db.InventoryReservations.ToListAsync());
        await Assert.ThrowsAsync<ConflictAppException>(() => c.PlaceAsync(new() { ClientRequestId = Guid.NewGuid(), QuoteToken = request.QuoteToken, Fingerprint = request.Fingerprint }));
        Assert.Equal(counts, await c.Web.CountsAsync());
    }

    [Fact]
    public async Task Concurrent_web_orders_have_globally_unique_W_references_even_across_store_scopes()
    {
        var a = await fixture.Web05Async(); var b = await fixture.Web05Async();
        var work = new List<(Web05Case Case, StorefrontPlaceOrderRequest Request)>();
        for (var i = 0; i < 6; i++) { var c = i % 2 == 0 ? a : b; work.Add((c, Web04Case.PlaceRequest(await c.QuoteAsync()))); }
        var results = await Task.WhenAll(work.Select(x => x.Case.PlaceAsync(x.Request)));
        Assert.All(results, x => Assert.Equal(StorefrontPlacementStatus.Placed, x.Status));
        await using var db = a.Context(); var references = await db.Set<BankPaymentReference>().AsNoTracking().Where(x => x.Channel == BankPaymentChannel.Storefront && (x.OriginStoreId == a.StoreId || x.OriginStoreId == b.StoreId)).ToListAsync();
        Assert.Equal(6, references.Count); Assert.Equal(6, references.Select(x => x.Reference).Distinct().Count()); Assert.All(references, x => Assert.Matches("^W[0-9]{12}$", x.Reference));
        Assert.All(references, x => Assert.Null(x.PosQrRequestId));
    }

    [Fact]
    public async Task Bank_binding_change_between_quote_and_place_does_not_retarget_old_quote_or_leave_order_and_holds()
    {
        var c = await fixture.Web05Async(); var quote = await c.QuoteAsync();
        await using (var db = c.Context()) await db.StoreBankAccounts.Where(x => x.Id == c.Seed.BankAccountId).ExecuteUpdateAsync(x => x.SetProperty(a => a.AccountNumber, "CHANGED-PHYSICAL-ACCOUNT"));
        await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(Web04Case.PlaceRequest(quote)));
        await using var check = c.Context(); Assert.Empty(await check.StorefrontOrders.ToListAsync()); Assert.Empty(await check.InventoryReservations.ToListAsync());
        Assert.Empty(await check.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await check.Set<BankPaymentReference>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
        Assert.Equal(0, (await check.InventoryBalances.SingleAsync()).ReservedQty);
    }

    [Theory]
    [InlineData(-1)][InlineData(2)][InlineData(999)]
    public async Task Undefined_payment_method_cannot_silently_fall_back_to_COD_or_create_quote_and_holds(int method)
    {
        var c = await fixture.Web05Async(); var request = c.Request(); request.PaymentMethod = (StorefrontPaymentMethod)method;
        await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync(request)); await using var db = c.Context(); Assert.Empty(await db.StorefrontQuotes.ToListAsync()); Assert.Empty(await db.StorefrontOrders.ToListAsync()); Assert.Empty(await db.InventoryReservations.ToListAsync());
    }

    [Fact]
    public async Task Explicit_COD_and_QR_choice_are_distinct_authoritative_quote_fingerprints_for_otherwise_identical_cart_and_recipient()
    {
        var c = await fixture.Web05Async(); var qr = await c.QuoteAsync(); var input = c.Request(); input.PaymentMethod = StorefrontPaymentMethod.Cod; var cod = await c.QuoteAsync(input);
        Assert.NotEqual(qr.Fingerprint, cod.Fingerprint); Assert.Equal(qr.TotalDue, cod.TotalDue); Assert.Equal(qr.Recipient, cod.Recipient);
        Assert.Equal(StorefrontPaymentMethod.Qr, qr.PaymentMethod); Assert.Equal(StorefrontPaymentMethod.Cod, cod.PaymentMethod);
    }
}
