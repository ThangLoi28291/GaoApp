using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06InventoryRaceTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false, false, 8)]
    [InlineData(false, false, 10)]
    [InlineData(false, false, 12)]
    [InlineData(false, true, 8)]
    [InlineData(true, false, 8)]
    [InlineData(true, true, 8)]
    public async Task Approved_physical_delivery_can_export_negative_stock_regardless_of_website_checkout_and_warehouse_policy(
        bool webAllows, bool warehouseAllows, int stockLoss)
    {
        var c = await Web06Case.CreateAsync(fixture, negative: webAllows, warehouseNegative: warehouseAllows);
        await c.PrepareAsync(); await c.PickAllAsync();
        // A real, explicitly permitted adjustment consumes stock after the accepted reservation.
        // It is not a forged balance update, source-order sale or a disabled stock guard.
        await using (var db = c.Context())
        {
            var adjustment = new InventoryMovementFactory().CreateAdjustmentDecrease(c.Seed.WarehouseId, c.Seed.VariantId, stockLoss, 10_000m, "WEB06 actual post-reservation stock loss");
            adjustment.AllowNegativeBalance = true;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(adjustment);
        }
        var command = await c.DispatchRequestAsync();
        await using (var db = c.Context())
        {
            await c.Service(db).DispatchAsync(c.OrderId, command);
        }
        var after = await c.PhysicalSnapshotAsync();
        await using (var retry = c.Context()) await c.Service(retry).DispatchAsync(c.OrderId, command);
        Assert.Equal(after, await c.PhysicalSnapshotAsync());
        await using var check = c.Context(); Assert.Equal(10m - stockLoss - 4m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.Equal(1, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
        Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Contains(await check.DeliveryDispatchCostFragments.ToListAsync(), x => x.IsProvisional);
        Assert.Equal(webAllows, (await check.DeliveryOrders.SingleAsync()).StorefrontAllowNegativeInventory);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Negative_dispatch_consumes_only_its_own_hold_and_preserves_another_real_web_order_hold(bool webAllows)
    {
        var c = await Web06Case.CreateAsync(fixture, stock: 10, negative: webAllows, warehouseNegative: false);
        var other = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(await c.Checkout.QuoteAsync(c.Checkout.Request(2)))); Assert.Equal(StorefrontPlacementStatus.Placed, other.Status);
        int otherOrder; await using (var db = c.Context()) otherOrder = await db.StorefrontOrders.Where(x => x.Code == other.Order!.Code).Select(x => x.Id).SingleAsync();
        await c.PrepareAsync(); await c.PickAllAsync();
        await using (var db = c.Context())
        {
            var adjustment = new InventoryMovementFactory().CreateAdjustmentDecrease(c.Seed.WarehouseId, c.Seed.VariantId, 10m, 10_000m, "Stock consumed after both website reservations");
            adjustment.AllowNegativeBalance = true;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(adjustment);
        }
        await c.DispatchAsync();
        await using var check = c.Context(); var balance = await check.InventoryBalances.SingleAsync(); Assert.Equal(-4m, balance.OnHandQty); Assert.Equal(2m, balance.ReservedQty);
        var otherReservation = await check.InventoryReservations.SingleAsync(x => x.ReferenceType == InventoryReferenceType.Storefront && x.ReferenceId == otherOrder.ToString());
        Assert.Equal(2m, otherReservation.ReservedQty); Assert.Equal(InventoryReservationStatus.Active, otherReservation.Status);
        Assert.All(await check.DeliveryDispatchCostFragments.ToListAsync(), x => Assert.True(x.IsProvisional));
    }

    [Fact]
    public async Task Concurrent_distinct_dispatch_requests_for_one_revision_produce_one_atomic_export_and_one_explicit_conflict()
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true); await c.PrepareAsync(); await c.PickAllAsync();
        var a = await c.DispatchRequestAsync(); var b = await c.DispatchRequestAsync(); var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Run(StorefrontDispatchRequest request)
        {
            await start.Task; await using var db = c.Context();
            try { await c.Service(db).DispatchAsync(c.OrderId, request); return true; } catch (ConflictAppException) { return false; }
        }
        var first = Run(a); var second = Run(b); start.SetResult(); var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(45));
        Assert.Single(results, x => x); await using var check = c.Context();
        Assert.Equal(2, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery)); Assert.Equal(-4m, await check.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).SumAsync(x => x.QuantityChange));
        Assert.Single(await check.Set<StorefrontCommandReceipt>().Where(x => x.Operation == "Dispatch").ToListAsync()); Assert.Equal(55_000m, await check.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_checkout_mode_snapshot_is_retained_when_store_mode_changes_before_sale(bool multi)
    {
        DateTime? originalActivation = null; DateTime? capturedAt = null; string addressSnapshot;
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true, quantity: multi ? 4 : 2, beforeQuote: async web =>
        {
            await using (var db = web.Context()) originalActivation = (await db.Stores.SingleAsync(x => x.Id == web.StoreId)).MultiLegalEntityActivatedAtUtc;
            if (!multi) { await using var db = web.Context(); await db.Stores.Where(x => x.Id == web.StoreId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsMultiLegalEntityEnabled, false)); }
        });
        await using (var db = c.Context())
        {
            var parent = await db.StorefrontOrders.SingleAsync(); Assert.Equal(multi, parent.UseMultiLegalEntitySnapshot); capturedAt = parent.LegalEntityModeCapturedAtUtc; Assert.NotNull(capturedAt);
            addressSnapshot = parent.AddressSnapshotJson; using var proof = System.Text.Json.JsonDocument.Parse(addressSnapshot);
            if (multi) { Assert.NotNull(originalActivation); Assert.Equal(originalActivation, proof.RootElement.GetProperty("legalEntityActivationAtUtcSnapshot").GetDateTime()); }
            else if (proof.RootElement.TryGetProperty("legalEntityActivationAtUtcSnapshot", out var inactive)) Assert.Equal(System.Text.Json.JsonValueKind.Null, inactive.ValueKind);
            await db.Stores.Where(x => x.Id == c.StoreId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsMultiLegalEntityEnabled, !multi));
        }
        await Web06FinanceSupport.ConfigureInvoiceOwnersAsync(c.Checkout); await Web06FinanceSupport.DeliveredAsync(c); await Web06FinanceSupport.CollectAllAsync(c);
        var total = multi ? 200_000m : 100_000m; var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(c);
        await Web06FinanceSupport.ReceiveCashAsync(c, await Web06FinanceSupport.ActualCashInAsync(c, terminal, total));
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(c, db, terminal).PostSaleAsync(c.OrderId, await c.RequestAsync<StorefrontOperationRequest>());
        await using var check = c.Context(); var sale = await check.Orders.SingleAsync(); Assert.Equal(multi, sale.UseMultiLegalEntity);
        Assert.Equal(capturedAt, sale.LegalEntityModeCapturedAtUtc); Assert.Equal(multi ? originalActivation : null, sale.LegalEntityActivationAtUtcSnapshot);
        Assert.Equal(addressSnapshot, (await check.StorefrontOrders.SingleAsync()).AddressSnapshotJson);
        Assert.Equal(multi ? 2 : 1, await check.OrderLegalEntityAllocations.Select(x => x.LegalEntityId).Distinct().CountAsync());
        Assert.Equal(multi ? 2 : 1, await check.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.Delivery));
    }
}
