using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb06"), Trait("Category", "StorefrontWeb06")]
public sealed class Web06PhysicalTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_COD_preparation_and_full_dispatch_use_native_source_and_one_typed_export_without_POS_or_money(bool national)
    {
        var c = await Web06Case.CreateAsync(fixture, national: national); var before = await c.PhysicalSnapshotAsync();
        await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        var detail = await c.DetailAsync(); Assert.Equal(StorefrontOrderPhase.Shipping, detail.Phase);
        Assert.All(detail.Children, x => Assert.Equal(StorefrontFulfillmentStage.Dispatched, x.Stage));
        await using var db = c.Context();
        Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSTerminals.ToListAsync());
        Assert.Empty(await db.OrderPayments.ToListAsync()); Assert.Empty(await db.Set<InvoiceHead>().ToListAsync());
        var child = Assert.Single(await db.DeliveryOrders.ToListAsync());
        Assert.Equal(DeliverySourceKind.Storefront, child.SourceKind); Assert.Equal(c.OrderId, child.StorefrontOrderId);
        Assert.Null(child.SourceCartId); Assert.Null(child.CreatedShiftId); Assert.Null(child.CreatedTerminalId);
        Assert.Equal(c.Seed.UserId, child.CreatedByUserId); Assert.True(child.StorefrontFulfillmentSourceId > 0);
        var exports = await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToListAsync();
        var export = Assert.Single(exports); Assert.Equal(-4m, export.QuantityChange); Assert.Equal(child.Id.ToString(), export.ReferenceId);
        Assert.Equal(InventoryTransactionType.DeliveryDispatchOut, export.TransactionType);
        Assert.Equal(6m, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.All(await db.InventoryReservations.ToListAsync(), x => Assert.Equal(InventoryReservationStatus.Consumed, x.Status));
        var transfer = Assert.Single(await db.Set<StorefrontReservationTransfer>().ToListAsync()); Assert.Equal(4m, transfer.BaseQuantity);
        var webHold = await db.InventoryReservations.SingleAsync(x => x.Id == transfer.WebReservationId);
        var deliveryHold = await db.InventoryReservations.SingleAsync(x => x.Id == transfer.DeliveryReservationId);
        Assert.Equal(InventoryReferenceType.Storefront, webHold.ReferenceType); Assert.Equal(InventoryReferenceType.Delivery, deliveryHold.ReferenceType);
        var journal = Assert.Single(await db.DeliveryJournalEntries.ToListAsync()); Assert.Equal(4m, journal.BaseQuantity); Assert.Equal(40_000m, journal.CostAmount);
        Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
        var slice = Assert.Single(await db.Set<StorefrontLineSource>().ToListAsync()); Assert.Equal(4m, slice.BaseQuantity);
        Assert.Equal(export.Id, slice.InventoryTransactionId); Assert.Equal(40_000m, slice.RecordedCost); Assert.False(slice.IsProvisionalCost);
        Assert.NotEqual(before, await c.PhysicalSnapshotAsync());
        if (national) { Assert.Null(detail.Children[0].CourierUserId); Assert.Equal("Synthetic carrier", detail.Children[0].CarrierName); Assert.False(string.IsNullOrWhiteSpace(detail.Children[0].TrackingCode)); }
        else { Assert.Equal(c.CourierId, detail.Children[0].CourierUserId); Assert.Null(detail.Children[0].CarrierName); }
    }

    [Fact]
    public async Task Same_original_line_splits_four_decimal_quantities_and_exact_FIFO_cost_across_two_actual_owners()
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true, quantity: 4.0001m); await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        var detail = await c.DetailAsync(); Assert.Equal(2, detail.Children.Count);
        Assert.Equal(new[] { "1.5001", "2.5" }, detail.Children.SelectMany(x => x.Lines).Select(x => x.BaseQuantity).Order().ToArray());
        Assert.Single(detail.Children.SelectMany(x => x.Lines).Select(x => x.OrderLineId).Distinct());
        await using var db = c.Context(); var children = await db.DeliveryOrders.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, children.Select(x => x.SourceWarehouseId).Distinct().Count()); Assert.Equal(2, children.Select(x => x.SourceLegalEntityId).Distinct().Count());
        Assert.Equal(200_005m, children.Sum(x => x.QuotedTotal)); Assert.Equal(4.0001m, children.SelectMany(x => x.Lines).Sum(x => x.StorefrontBaseQuantity ?? -1));
        var fragments = await db.DeliveryDispatchCostFragments.ToListAsync(); Assert.Equal(4.0001m, fragments.Sum(x => x.BaseQuantity));
        Assert.Equal(55_002m, fragments.Sum(x => x.CostAmount)); Assert.All(fragments, x => Assert.False(x.IsProvisional));
        Assert.Equal(8.4999m, await db.InventoryBalances.SumAsync(x => x.OnHandQty)); Assert.Equal(0m, await db.InventoryBalances.SumAsync(x => x.ReservedQty));
        Assert.Equal(4.0001m, await db.Set<StorefrontLineSource>().SumAsync(x => x.BaseQuantity));
        Assert.Equal(55_002m, await db.Set<StorefrontLineSource>().SumAsync(x => x.RecordedCost));
    }

    [Fact]
    public async Task Pack_factor100_uses_actual_base200_in_native_picking_bounds_and_exports_once()
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: 2m, stock: 250m, usePack: true, packFactor: 100m);
        await c.PrepareAsync(); var picking = await c.PickingAsync(); Assert.Equal("200", Assert.Single(picking.Lines).BaseQuantity);
        await using (var db = c.Context()) await c.Service(db).RecordPickingAsync(c.OrderId, picking);
        await c.DispatchAsync(); await using var verify = c.Context();
        var line = await verify.Set<StorefrontOrderLine>().SingleAsync(); Assert.Equal(2m, line.Quantity); Assert.Equal(200m, line.BaseQuantity);
        Assert.Equal(5_000_000m, line.UnitPrice); Assert.Equal(10_000_000m, line.NetAmount);
        Assert.Equal(200m, (await verify.DeliveryPickingLines.SingleAsync()).ReportedQuantity);
        Assert.Equal(200m, (await verify.DeliveryPickingLines.SingleAsync()).ApprovedQuantity);
        Assert.Equal(-200m, (await verify.InventoryTransactions.SingleAsync(x => x.ReferenceType == InventoryReferenceType.Delivery)).QuantityChange);
        Assert.Equal(50m, (await verify.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Fact]
    public async Task Non_gift_whole_VND_split_may_have_zero_net_child_but_parent_and_prices_remain_sealed_positive()
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: 2m, multipleSources: true, primaryMultiStock: 1m, unitPrice: 0.4m);
        await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        await using var db = c.Context(); var order = await db.StorefrontOrders.SingleAsync(); Assert.Equal(1m, order.GoodsAmount);
        var original = await db.Set<StorefrontOrderLine>().SingleAsync(); Assert.False(original.IsGift); Assert.Equal(0.4m, original.UnitPrice);
        var children = await db.DeliveryOrders.ToListAsync(); Assert.Equal(new[] { 0m, 1m }, children.Select(x => x.QuotedTotal).Order().ToArray());
        Assert.Equal(2m, await db.DeliveryJournalEntries.Where(x => x.Kind == DeliveryJournalKind.Dispatch).SumAsync(x => x.BaseQuantity));
        Assert.Equal(2, await db.DeliveryPickingWorks.CountAsync()); Assert.Equal(new[] { 0m, 1m }, (await db.DeliveryPickingWorks.ToListAsync()).Select(x => x.ApprovedTotal!.Value).Order().ToArray());
        Assert.Empty(await db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Actual_same_SKU_promotion_gift_only_owner_child_has_zero_money_and_full_real_quantity_cost()
    {
        var c = await Web06Case.CreateAsync(fixture, quantity: 2m, multipleSources: true, primaryMultiStock: 2m, unitPrice: 100_000m, sameSkuGift: true);
        await Web06Support.AssertPurchasedTwoGiftParityAsync(c, 200_000m);
        await using (var db = c.Context())
        {
            var lines = await db.Set<StorefrontOrderLine>().OrderBy(x => x.Id).ToListAsync(); Assert.Equal(2, lines.Count);
            Assert.Equal(200_000m, lines.Sum(x => x.NetAmount)); Assert.Equal(3m, lines.Sum(x => x.BaseQuantity));
            var gift = Assert.Single(lines, x => x.IsGift); Assert.Equal(0m, gift.NetAmount); Assert.Equal(1m, gift.BaseQuantity);
            using var snapshot = System.Text.Json.JsonDocument.Parse(gift.SnapshotJson);
            using var promotion = System.Text.Json.JsonDocument.Parse(snapshot.RootElement.GetProperty("pricing").GetProperty("promotionSnapshotJson").GetString()!);
            Assert.NotEmpty(promotion.RootElement.GetProperty("programs").EnumerateArray());
        }
        await c.PrepareAsync(); await c.PickAllAsync(); await c.DispatchAsync();
        await using (var db = c.Context())
        {
            Assert.Equal(new[] { 0m, 200_000m }, (await db.DeliveryOrders.ToListAsync()).Select(x => x.QuotedTotal).Order().ToArray());
            Assert.Equal(3m, await db.DeliveryJournalEntries.Where(x => x.Kind == DeliveryJournalKind.Dispatch).SumAsync(x => x.BaseQuantity));
            Assert.Equal(40_000m, await db.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount));
            Assert.Single(await db.Set<StorefrontOrderLine>().Where(x => x.IsGift && x.NetAmount == 0).ToListAsync());
            Assert.Empty(await db.Orders.ToListAsync()); // Financial bridge will create one parent sale, never a zero child sale.
        }
    }

    [Theory]
    [InlineData("0")] [InlineData("1.2345")]
    public async Task Actual_partial_picking_has_valid_report_but_no_approval_or_price_change_and_cannot_dispatch(string picked)
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); var request = await c.PickingAsync();
        request.Lines[0] = request.Lines[0] with { BaseQuantity = picked };
        await using (var db = c.Context()) await c.Service(db).RecordPickingAsync(c.OrderId, request);
        await using (var db = c.Context())
        {
            var row = await db.DeliveryPickingLines.SingleAsync(); Assert.Equal(decimal.Parse(picked, System.Globalization.CultureInfo.InvariantCulture), row.ReportedQuantity);
            Assert.Null(row.ApprovedQuantity); Assert.Null(row.ApprovedOriginalCoverage); Assert.Null(row.ApprovedNet);
            Assert.Equal("picker-report", row.ReportFactKind); Assert.False(string.IsNullOrWhiteSpace(row.ShortageReason));
            Assert.Equal(200_000m, (await db.StorefrontOrders.SingleAsync()).GoodsAmount);
        }
        var before = await c.PhysicalSnapshotAsync(); var dispatch = await c.DispatchRequestAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).DispatchAsync(c.OrderId, dispatch));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Theory]
    [InlineData("-1")] [InlineData("1,25")] [InlineData("1e1")] [InlineData("1.23456")] [InlineData("NaN")] [InlineData("01")]
    public async Task Malformed_actual_quantity_is_rejected_before_any_picking_effect(string text)
    {
        var c = await Web06Case.CreateAsync(fixture); await c.PrepareAsync(); var request = await c.PickingAsync();
        request.Lines[0] = request.Lines[0] with { BaseQuantity = text }; var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ValidationAppException>(() => c.Service(db).RecordPickingAsync(c.OrderId, request));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Fact]
    public async Task Second_child_actual_inventory_save_failure_rolls_back_all_children_costs_holds_events_and_retry_succeeds_once()
    {
        var c = await Web06Case.CreateAsync(fixture, multipleSources: true); await c.PrepareAsync(); await c.PickAllAsync();
        var detail = await c.DetailAsync(); var target = detail.Children.Select(x => x.DeliveryOrderId).Max();
        var request = await c.DispatchRequestAsync(); var before = await c.PhysicalSnapshotAsync(); var failure = new SecondExportFailure(target);
        await using (var db = c.Context(failure))
        {
            var service = c.Service(db); await Assert.ThrowsAsync<SyntheticDispatchException>(() => service.DispatchAsync(c.OrderId, request));
            Assert.True(failure.Fired); Assert.Equal(before, await c.PhysicalSnapshotAsync());
            await service.DispatchAsync(c.OrderId, request); // Same clean scoped service/context, not only a new provider.
        }
        await using (var db = c.Context())
        {
            var replay = await c.Service(db).DispatchAsync(c.OrderId, request); Assert.True(replay.Replayed);
            Assert.Equal(2, await db.DeliveryJournalEntries.CountAsync(x => x.Kind == DeliveryJournalKind.Dispatch));
            Assert.Equal(-4m, await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).SumAsync(x => x.QuantityChange));
        }
    }

    [Fact]
    public async Task Operation_scoped_same_GUID_and_exact_replays_survive_later_operation_and_new_context()
    {
        var c = await Web06Case.CreateAsync(fixture); var key = Guid.NewGuid(); var prepare = await c.RequestAsync<StorefrontOperationRequest>(key);
        StorefrontOperationAck started;
        await using (var db = c.Context()) started = await c.Service(db).StartPreparationAsync(c.OrderId, prepare);
        var pick = await c.PickingAsync(key: key);
        await using (var db = c.Context()) await c.Service(db).RecordPickingAsync(c.OrderId, pick);
        await using (var db = c.Context())
        {
            var replay = await c.Service(db).StartPreparationAsync(c.OrderId, prepare); Assert.True(replay.Replayed); Assert.Equal(started.Revision, replay.Revision);
            var picked = await c.Service(db).RecordPickingAsync(c.OrderId, pick); Assert.True(picked.Replayed);
            var receipts = await db.Set<StorefrontCommandReceipt>().Where(x => x.ClientRequestId == key).ToListAsync();
            Assert.Equal(new[] { "RecordPicking", "StartPreparation" }, receipts.Select(x => x.Operation).Order().ToArray());
            Assert.Single(await db.DeliveryOrders.ToListAsync());
        }
        var before = await c.PhysicalSnapshotAsync(); prepare.Note = "Different same-operation payload";
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).StartPreparationAsync(c.OrderId, prepare));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Theory]
    [InlineData("permission")] [InlineData("anonymous")] [InlineData("membership")]
    public async Task Service_denial_keeps_all_physical_and_history_rows_unchanged(string kind)
    {
        var c = await Web06Case.CreateAsync(fixture); var request = await c.RequestAsync<StorefrontOperationRequest>();
        if (kind == "membership") { await using var db = c.Context(); await db.UserInStores.Where(x => x.UserId == c.Seed.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)); }
        var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() => c.Service(db,
            kind == "permission" ? [StorefrontPermissions.ViewOrders] : null, authenticated: kind != "anonymous").StartPreparationAsync(c.OrderId, request));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Fact]
    public async Task Stale_revision_and_version_fail_without_repeat_preparation_or_partial_child()
    {
        var c = await Web06Case.CreateAsync(fixture); var old = await c.RequestAsync<StorefrontOperationRequest>(); await c.PrepareAsync();
        var before = await c.PhysicalSnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => c.Service(db).StartPreparationAsync(c.OrderId, old));
        Assert.Equal(before, await c.PhysicalSnapshotAsync());
    }

    [Fact]
    public async Task Known_checkout_and_reported_picking_shortages_have_same_per_order_truth_in_all_and_filtered_queues()
    {
        var c = await Web06Case.CreateAsync(fixture, stock: 4m, negative: true);
        await using (var initial = c.Context()) Assert.False(Assert.Single((await c.Service(initial).ListAsync(new())).Items, x => x.Id == c.OrderId).HasInventoryShortage);
        c.Checkout.Clock.Utc = DateTime.UtcNow; var quote = await c.Checkout.QuoteAsync(c.Checkout.Request(1m)); Assert.True(quote.HasInventoryShortage);
        var second = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.NotNull(second.Order);
        await using (var db = c.Context())
        {
            var secondId = await db.StorefrontOrders.Where(x => x.Code == second.Order.Code).Select(x => x.Id).SingleAsync();
            var service = c.Service(db); var all = await service.ListAsync(new()); var shortOnly = await service.ListAsync(new(ShortageOnly: true));
            Assert.True(Assert.Single(all.Items, x => x.Id == c.OrderId).HasInventoryShortage);
            Assert.True(Assert.Single(all.Items, x => x.Id == secondId).HasInventoryShortage);
            Assert.Equal(2, shortOnly.Items.Count); Assert.All(shortOnly.Items, x => Assert.True(x.HasInventoryShortage));
            Assert.Contains(shortOnly.Items, x => x.Id == c.OrderId); Assert.Contains(shortOnly.Items, x => x.Id == secondId);
            Assert.Empty((await service.ListAsync(new(PaymentMethod: StorefrontPaymentMethod.Qr))).Items);
            Assert.Empty((await service.ListAsync(new(DeliveryMethod: StorefrontDeliveryMethod.Nationwide))).Items);
        }
        await c.PrepareAsync(); var partial = await c.PickingAsync(3m);
        await using (var db = c.Context()) await c.Service(db).RecordPickingAsync(c.OrderId, partial);
        await using (var db = c.Context())
        {
            var all = await c.Service(db).ListAsync(new()); var shortOnly = await c.Service(db).ListAsync(new(ShortageOnly: true));
            Assert.Equal(2, shortOnly.Items.Count); Assert.All(shortOnly.Items, x => Assert.True(x.HasInventoryShortage));
            Assert.All(all.Items, x => Assert.Equal(Assert.Single(shortOnly.Items, y => y.Id == x.Id).HasInventoryShortage, x.HasInventoryShortage));
        }
    }

    private sealed class SyntheticDispatchException : Exception { }
    private sealed class SecondExportFailure(int deliveryId) : SaveChangesInterceptor
    {
        internal bool Fired;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context!.ChangeTracker.Entries<InventoryTransaction>().Any(x => x.State == EntityState.Added &&
                x.Entity.ReferenceType == InventoryReferenceType.Delivery && x.Entity.ReferenceId == deliveryId.ToString()))
            { Fired = true; throw new SyntheticDispatchException(); }
            return ValueTask.FromResult(result);
        }
    }
}
