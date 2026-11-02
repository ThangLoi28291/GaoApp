using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Orders.LegalEntityAllocation;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04PlaceTests(Web01Fixture fixture)
{
    [Fact]
    public async Task COD_received_atomically_has_typed_holds_and_snapshots_but_no_stock_or_financial_posting_or_POS_context()
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync(c.Request(2, pack: true)); var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote));
        Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status); Assert.Null(placed.ReplacementQuote); var result = Assert.IsType<StorefrontPlacedOrder>(placed.Order);
        var token = Web04Support.GuestToken(result); Assert.Equal(32, WebEncoders.Base64UrlDecode(token).Length);
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync();
        Assert.Equal(StorefrontOrderPhase.AwaitingPreparation, order.Phase); Assert.Equal(StorefrontPaymentMethod.Cod, order.PaymentMethod);
        Assert.Equal(StorefrontPaymentState.Unpaid, order.PaymentState); Assert.Equal(StorefrontDeliveryState.NotPlanned, order.DeliveryState);
        Assert.Equal(StorefrontRefundState.None, order.RefundState); Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, order.ShippingFeeTreatment);
        Assert.Equal(quote.GoodsAmount, order.GoodsAmount); Assert.Equal(quote.TotalDue, order.TotalDue); Assert.Equal(result.Code, order.Code);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), order.GuestTokenHash); Assert.DoesNotContain(token, order.AddressSnapshotJson);
        var line = await db.Set<StorefrontOrderLine>().SingleAsync(); Assert.Equal(24, line.BaseQuantity); Assert.Equal(180000, line.UnitPrice);
        Assert.Equal(360000, line.NetAmount); Assert.Equal(quote.Lines[0].RoundingAdjustment, line.RoundingAdjustment);
        var hold = await db.InventoryReservations.SingleAsync(); Assert.Equal(InventoryReferenceType.Storefront, hold.ReferenceType);
        Assert.Equal(order.Id, hold.StorefrontOrderId); Assert.Equal(line.Id, hold.StorefrontOrderLineId); Assert.Equal(c.Seed.LegalEntityId, hold.SourceLegalEntityId);
        Assert.Equal(24, hold.ReservedQty); Assert.Equal(InventoryReservationStatus.Active, hold.Status); Assert.Null(hold.DeliveryOrderId);
        var link = await db.Set<StorefrontReservationLink>().SingleAsync(); Assert.Equal(hold.Id, link.InventoryReservationId); Assert.Equal(line.Id, link.OrderLineId);
        var source = await db.Set<StorefrontFulfillmentSource>().SingleAsync(); Assert.Equal(c.Seed.WarehouseId, source.SourceWarehouseId); Assert.Equal(source.Id, hold.StorefrontFulfillmentSourceId);
        var balance = await db.InventoryBalances.SingleAsync(); Assert.Equal(100, balance.OnHandQty); Assert.Equal(24, balance.ReservedQty); Assert.Equal(0, balance.InventoryValue);
        var ev = await db.Set<StorefrontEvent>().SingleAsync(); var message = await db.Set<StorefrontOutboxMessage>().SingleAsync(); Assert.Equal("OrderCreated", ev.Kind); Assert.Equal(ev.EventId, message.EventId);
        var receipt = await db.Set<StorefrontCommandReceipt>().SingleAsync(); Assert.DoesNotContain(token, receipt.OutcomeJson); Assert.DoesNotContain(token, message.PayloadJson); Assert.DoesNotContain(token, ev.PayloadJson);
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSTerminals.ToListAsync());
        Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        Assert.Empty(await db.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Empty(await db.Set<StorefrontBankReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData("price")][InlineData("shipping")][InlineData("settings")][InlineData("address")][InlineData("expiry")][InlineData("promotion")]
    public async Task Stale_quote_requires_explicit_reconfirmation_and_creates_no_order_hold_or_receipt(string change)
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync(); var before = await c.CountsAsync();
        switch (change)
        {
            case "price": await Web04PromotionTests.SetBasePrice(c, 27000); break;
            case "shipping": await c.RateAsync(21000); break;
            case "settings": await c.SettingsAsync(negative: false); break;
            case "address": await c.WriteAddressAsync("SYNTHETIC-W04-V2"); break;
            case "expiry": c.Clock.Utc = quote.ExpiresAtUtc.AddSeconds(1); break;
            case "promotion": await Web04PromotionTests.Promotion(c, new() { Name = "New10", Type = PromotionType.ProductDiscount, DiscountType = PromotionDiscountType.Percentage,
                DiscountValue = 10, Items = [new() { ProductId = c.Seed.ProductId }] }); break;
        }
        var afterChange = await c.CountsAsync(); Assert.Equal(before, afterChange);
        var request = Web04Case.PlaceRequest(quote); var changed = await c.PlaceAsync(request); Assert.Equal(StorefrontPlacementStatus.QuoteChanged, changed.Status); Assert.Null(changed.Order);
        var replacement = Assert.IsType<StorefrontQuoteDto>(changed.ReplacementQuote); Assert.NotEqual(quote.Token, replacement.Token); Assert.Equal(afterChange, await c.CountsAsync());
        if (change != "expiry") Assert.NotEqual(quote.Fingerprint, replacement.Fingerprint);
        var accepted = await c.PlaceAsync(Web04Case.PlaceRequest(replacement, request.ClientRequestId)); Assert.Equal(StorefrontPlacementStatus.Placed, accepted.Status);
        Assert.Equal(replacement.TotalDue, accepted.Order!.TotalDue);
    }

    [Theory]
    [InlineData("hidden")][InlineData("closed")][InlineData("addressMissing")][InlineData("codOff")][InlineData("sourceInactive")]
    public async Task Quote_does_not_authorize_purchase_after_live_eligibility_is_revoked(string change)
    {
        var c = await fixture.Web04Async(); var quote = await c.QuoteAsync();
        await using (var db = c.Context()) switch (change)
        {
            case "hidden": await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false)); break;
            case "closed": await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsEnabled, false)); break;
            case "codOff": await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(p => p.CodEnabled, false)); break;
            case "sourceInactive": await db.Warehouses.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsActive, false)); break;
            case "addressMissing": File.Delete(c.AddressFile); break;
        }
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(Web04Case.PlaceRequest(quote))); Assert.Equal(before, await c.CountsAsync());
    }

    [Theory]
    [InlineData(1, false, false)][InlineData(0, false, true)][InlineData(-1, false, true)]
    [InlineData(0, true, false)][InlineData(-1, true, false)]
    public async Task Live_available_inventory_policy_handles_positive_zero_and_negative_without_physical_posting(decimal onHand, bool allowNegative, bool rejected)
    {
        var c = await fixture.Web04Async(); await c.SettingsAsync(negative: allowNegative);
        // Start with a valid quote, then lose stock. This checks placement's
        // fresh predicate independently of the quote's own early rejection.
        var quote = await c.QuoteAsync(); await c.StockAsync(onHand);
        if (!allowNegative && onHand < 1) await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync());
        else { var current = await c.QuoteAsync(); Assert.Equal(onHand < 1, current.HasInventoryShortage); }
        var before = await c.CountsAsync(); var request = Web04Case.PlaceRequest(quote);
        if (rejected) { await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(request)); Assert.Equal(before, await c.CountsAsync()); }
        else
        {
            var placed = await c.PlaceAsync(request); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status); await using var db = c.Context();
            var balance = await db.InventoryBalances.SingleAsync(); Assert.Equal(onHand, balance.OnHandQty); Assert.Equal(1, balance.ReservedQty); Assert.Empty(await db.InventoryTransactions.ToListAsync());
        }
    }

    [Fact]
    public async Task Existing_POS_and_delivery_holds_are_preserved_and_reduce_available_stock()
    {
        var c = await fixture.Web04Async(); await c.SettingsAsync(negative: false); await c.StockAsync(10, 7);
        int first, second; await using (var db = c.Context())
        {
            var a = Hold(c, InventoryReferenceType.Order, 3, "SYNTHETIC-POS-HOLD"); db.Add(a); await db.SaveChangesAsync(); first = a.Id;
            second = await LegacyDeliveryHold(c, db, 4);
        }
        var quote = await c.QuoteAsync(c.Request(3)); Assert.False(quote.HasInventoryShortage); await c.PlaceAsync(Web04Case.PlaceRequest(quote));
        await using (var db = c.Context())
        {
            var holds = await db.InventoryReservations.OrderBy(x => x.Id).ToListAsync(); Assert.Equal(3, holds.Count);
            Assert.Equal(3, holds.Single(x => x.Id == first).ReservedQty); Assert.Equal(4, holds.Single(x => x.Id == second).ReservedQty);
            Assert.All(holds, x => Assert.Equal(InventoryReservationStatus.Active, x.Status)); Assert.Equal(10, (await db.InventoryBalances.SingleAsync()).ReservedQty);
        }
        var before = await c.CountsAsync(); await Assert.ThrowsAsync<ValidationAppException>(() => c.QuoteAsync()); Assert.Equal(before, await c.CountsAsync());
    }

    [Fact]
    public async Task Corrupt_shared_hold_counter_fails_closed_instead_of_overwriting_existing_reservations()
    {
        var c = await fixture.Web04Async(); await c.StockAsync(100, 7); var quote = await c.QuoteAsync(); var before = await c.CountsAsync();
        await Assert.ThrowsAsync<ValidationAppException>(() => c.PlaceAsync(Web04Case.PlaceRequest(quote))); Assert.Equal(before, await c.CountsAsync());
    }
    [Fact]
    public async Task Two_concurrent_distinct_requests_for_last_unit_cannot_overreserve()
    {
        var c = await fixture.Web04Async(); await c.SettingsAsync(negative: false); await c.StockAsync(1);
        var a = Web04Case.PlaceRequest(await c.QuoteAsync()); var b = Web04Case.PlaceRequest(await c.QuoteAsync());
        async Task<bool> Try(StorefrontPlaceOrderRequest request) { try { return (await c.PlaceAsync(request)).Status == StorefrontPlacementStatus.Placed; } catch (ValidationAppException) { return false; } }
        var results = await Task.WhenAll(Try(a), Try(b)); Assert.Single(results, x => x); await using var db = c.Context();
        Assert.Single(await db.StorefrontOrders.ToListAsync()); Assert.Single(await db.InventoryReservations.ToListAsync()); Assert.Equal(1, (await db.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Equal(1, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Single(await db.Set<StorefrontCommandReceipt>().ToListAsync());
    }
    [Fact]
    public async Task Failure_after_order_insert_rolls_back_entire_order_and_can_retry_same_key()
    {
        var c = await fixture.Web04Async(); var request = Web04Case.PlaceRequest(await c.QuoteAsync()); var before = await c.CountsAsync(); var fault = new AllocationFailure();
        await using (var db = c.Context()) await Assert.ThrowsAsync<InvalidOperationException>(() => c.Service(db, allocation: fault).PlaceAsync(request));
        Assert.True(fault.SawPersistedWebOrderIdentity); Assert.Equal(before, await c.CountsAsync());
        var retry = await c.PlaceAsync(request); Assert.Equal(StorefrontPlacementStatus.Placed, retry.Status); await using var verify = c.Context(); Assert.Single(await verify.StorefrontOrders.ToListAsync());
    }
    private static InventoryReservation Hold(Web04Case c, InventoryReferenceType type, decimal quantity, string id) => new()
    { StoreId = c.StoreId, WarehouseId = c.Seed.WarehouseId, ProductVariantId = c.Seed.VariantId, ReferenceType = type, ReferenceId = id, ReservedQty = quantity, Status = InventoryReservationStatus.Active };
    // An actual legacy source graph lives only in this GUID fixture. The web
    // service must leave every source/report fact and held quantity untouched.
    private static async Task<int> LegacyDeliveryHold(Web04Case c, GaoApp.Infrastructure.Data.AppDbContext db, decimal quantity)
    {
        var terminal = new POSTerminal { StoreId = c.StoreId, Code = "W04LEGACY", Name = "WEB04 existing legacy context" }; db.Add(terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = c.StoreId, TerminalId = terminal.Id, OpenedByUserId = c.Seed.UserId, WarehouseId = c.Seed.WarehouseId }; db.Add(shift); await db.SaveChangesAsync();
        var cart = new Order { StoreId = c.StoreId, POSShiftId = shift.Id, Subtotal = 100000, GrandTotal = 100000, BalanceDue = 100000 }; db.Add(cart); await db.SaveChangesAsync();
        var sourceLine = new OrderLine { StoreId = c.StoreId, OrderId = cart.Id, ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId,
            ItemName = "Legacy fixture", UnitName = "Cái", Quantity = quantity, Multiplier = 1, BaseQuantity = quantity, UnitPrice = 25000, LineTotal = 100000 };
        db.Add(sourceLine); await db.SaveChangesAsync();
        var delivery = new DeliveryOrder { StoreId = c.StoreId, Code = "W04-L-" + Guid.NewGuid().ToString("N"), LookupToken = Guid.NewGuid().ToString("N"), State = DeliveryState.Picking,
            SourceCartId = cart.Id, SourceWarehouseId = c.Seed.WarehouseId, SourceLegalEntityId = c.Seed.LegalEntityId, CreatedTerminalId = terminal.Id, CreatedShiftId = shift.Id,
            CreatedByUserId = c.Seed.UserId, RecipientName = "Legacy fixture", RecipientPhone = "0901234567", RecipientAddress = "Synthetic", QuotedTotal = 100000 };
        db.Add(delivery); await db.SaveChangesAsync();
        var line = new DeliveryOrderLine { StoreId = c.StoreId, DeliveryOrderId = delivery.Id, SourceCartId = cart.Id, SourceOrderLineId = sourceLine.Id, VariantId = c.Seed.VariantId,
            ItemName = "Legacy fixture", UnitName = "Cái", BaseUnitName = "Cái", OrderedQuantity = quantity, BaseMultiplier = 1, UnitPrice = 25000, Gross = 100000, Net = 100000 };
        db.Add(line); await db.SaveChangesAsync(); var reported = DateTime.UtcNow;
        db.Add(new DeliveryPickingWork { StoreId = c.StoreId, DeliveryOrderId = delivery.Id, PickerUserId = c.Seed.UserId,
            AssignedAtUtc = reported, StartedAtUtc = reported }); await db.SaveChangesAsync();
        db.Add(new DeliveryPickingLine { StoreId = c.StoreId, DeliveryOrderId = delivery.Id, DeliveryOrderLineId = line.Id, PlannedQuantity = quantity, PlannedOriginalCoverage = 0,
            ReportedQuantity = quantity, ReporterUserId = c.Seed.UserId, ReportedAtUtc = reported, ReportFactKind = "picker-report" }); await db.SaveChangesAsync();
        var hold = Hold(c, InventoryReferenceType.Delivery, quantity, delivery.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        hold.DeliveryOrderId = delivery.Id; hold.DeliveryOrderLineId = line.Id; hold.ReferenceLineId = line.Id; hold.SourceLegalEntityId = c.Seed.LegalEntityId;
        hold.DeliveryRevision = 1; hold.ReportedByUserId = c.Seed.UserId; hold.ReportedAtUtc = reported; db.Add(hold); await db.SaveChangesAsync(); return hold.Id;
    }
    private sealed class AllocationFailure : IOrderLegalEntityAllocationService
    {
        internal bool SawPersistedWebOrderIdentity { get; private set; }
        public OrderLegalEntityAllocationResult Preview(OrderLegalEntityAllocationRequest request)
        { SawPersistedWebOrderIdentity = request.OrderId > 0; throw new InvalidOperationException("WEB04 injected allocation outage after order insert"); }
    }
}
