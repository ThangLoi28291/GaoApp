using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GaoApp.Tests.Inventory;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD02"), Trait("Category", "DeliveryPriceStockCompatibility")]
public sealed class DeliveryPriceStockCompatibilityTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData("line", 24000)]
    [InlineData("combo", 15000)]
    [InlineData("mixed", 20000)]
    [InlineData("gift", 20000)]
    public async Task P01_Actual_POS_promotions_and_free_gifts_freeze_the_exact_total_without_counting_combo_twice(string kind, int expected)
    {
        using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, kind);
        Assert.Equal(expected, c.PricedOrder.GrandTotal);
        if (kind == "line") Assert.True(c.PricedOrder.Lines.Sum(x => x.PromotionDiscount) > 0);
        if (kind == "combo") Assert.True(c.PricedOrder.ComboDiscountTotal > 0);
        if (kind == "mixed")
        {
            // Mixed quantity discounts are allocated on lines, never counted twice on the order.
            Assert.Equal(10000m, c.PricedOrder.Lines.Where(x => !x.IsDeleted).Sum(x => x.PromotionDiscount));
            Assert.Equal(0m, c.PricedOrder.ComboDiscountTotal);
        }
        var request = await DeliveryD03Support.Request(c.Source);
        var result = await c.CreateAsync(request);
        Assert.Equal(c.PricedOrder.GrandTotal, result.Delivery.QuotedTotal);
        Assert.Equal(result.Delivery.QuotedTotal, result.Delivery.Lines.Sum(x => x.Net));
        Assert.Equal(c.PricedOrder.Lines.Where(x => !x.IsDeleted).Select(x => x.Id).Order(),
            result.Delivery.Lines.Select(x => x.SourceOrderLineId!.Value).Order());
        foreach (var quote in result.Delivery.Lines)
        {
            var pos = Assert.Single(c.PricedOrder.Lines, x => x.Id == quote.SourceOrderLineId);
            Assert.Equal(pos.Quantity, quote.OrderedQuantity); Assert.Equal(pos.Multiplier, quote.BaseMultiplier);
            Assert.Equal(pos.UnitPrice, quote.UnitPrice);
            Assert.Equal(quote.Gross - quote.LineDiscount - quote.AllocatedOrderDiscount, quote.Net);
            Assert.True(quote.Net >= 0);
        }
        if (kind == "gift")
        {
            var gift = Assert.Single(c.PricedOrder.Lines, x => x.IsPromotionGift && !x.IsDeleted);
            var quote = Assert.Single(result.Delivery.Lines, x => x.SourceOrderLineId == gift.Id);
            Assert.Equal(1, quote.OrderedQuantity); Assert.Equal(0, quote.UnitPrice); Assert.Equal(0, quote.Net);
        }
        var replay = await c.CreateAsync(request);
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(replay));
        await using var db = c.Source.Context();
        Assert.Equal(1, await db.DeliveryOrders.CountAsync(x => x.SourceCartId == c.Source.CartId));
        Assert.Empty(await db.OrderPayments.Where(x => x.OrderId == c.Source.CartId).ToArrayAsync());
        Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ReferenceType == InventoryReferenceType.Delivery && x.ReferenceId == result.Delivery.Id.ToString()));
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("gift")]
    public async Task P02_Partial_picking_keeps_agreed_unit_and_gift_prices_after_manager_approval_without_repricing_eligibility(string kind)
    {
        using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, kind);
        await c.CreateAsync(); var initial = c.Source.Detail;
        var approved = await c.ReadyAsync(partial: true);
        Assert.Equal(nameof(DeliveryState.ReadyForHandover), approved.Delivery.State);
        Assert.Equal("10000", approved.ApprovedTotalText);
        Assert.Equal(initial.QuotedTotal, approved.Delivery.QuotedTotal);
        foreach (var line in approved.Lines)
        {
            var original = Assert.Single(initial.Lines, x => x.Id == line.LineId);
            Assert.Equal(original.UnitPrice, decimal.Parse(line.UnitPriceText, CultureInfo.InvariantCulture));
            Assert.Equal(original.Net, decimal.Parse(line.QuotedNetText, CultureInfo.InvariantCulture));
            var approvedQty = decimal.Parse(line.ApprovedQuantityText!, CultureInfo.InvariantCulture);
            var expected = DeliveryValues.RoundMoney(approvedQty / original.OrderedQuantity * original.Net);
            Assert.Equal(expected, decimal.Parse(line.ApprovedNetText!, CultureInfo.InvariantCulture));
        }
        if (kind == "gift") Assert.Single(approved.Lines, x => decimal.Parse(x.UnitPriceText, CultureInfo.InvariantCulture) == 0 && decimal.Parse(x.ApprovedNetText!, CultureInfo.InvariantCulture) == 0);
        await c.Inventory.DispatchAsync();
        await using var db = c.Source.Context();
        var journals = await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync();
        Assert.Equal(approved.Lines.Sum(x => decimal.Parse(x.ApprovedBaseQuantityText!, CultureInfo.InvariantCulture)), journals.Sum(x => x.BaseQuantity));
        Assert.All(journals, x => { Assert.Equal(0, x.MoneyAmount); Assert.Null(x.SaleOrderId); });
    }

    [Fact]
    public async Task P03_Pending_delivery_reserves_an_applied_voucher_blocks_other_POS_and_delivery_then_releases_on_cancellation()
    {
        using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, "combo");
        await c.ApplyVoucherAsync(); Assert.Equal(12000, c.PricedOrder.GrandTotal);
        Assert.Equal(5000, c.PricedOrder.ComboDiscountTotal); Assert.Equal(3000, c.PricedOrder.VoucherDiscountTotal);
        using var other = await c.OtherCartAsync(fixture);
        var otherRequest = await DeliveryD03Support.Request(other);
        var request = await DeliveryD03Support.Request(c.Source);
        var created = await c.CreateAsync(request); Assert.Equal(12000, created.Delivery.QuotedTotal);
        Assert.Equal(12000, created.Delivery.Lines.Sum(x => x.Net));
        await AssertReservedVoucher(c);
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(await c.CreateAsync(request)));
        using (var unavailable = await other.Client.Http.PostAsJsonAsync("/admin/pos/cart/current/reward-vouchers", new { voucherIds = new[] { c.VoucherId!.Value } }))
            Assert.False(unavailable.IsSuccessStatusCode);
        using (var duplicate = await other.Client.Http.PostAsJsonAsync(DeliveryD03Support.CreatePath, otherRequest))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using (var payment = await other.Client.Http.PostAsJsonAsync("/admin/pos/cart/current/payment-and-finalize",
            new { orderId = other.CartId, clientRequestId = Guid.NewGuid(), method = 0, amount = 27000 }))
            Assert.False(payment.IsSuccessStatusCode);
        await using (var verify = c.Source.Context())
        {
            Assert.Empty(await verify.OrderPayments.Where(x => x.OrderId == other.CartId).ToArrayAsync());
            Assert.False(await verify.DeliveryOrders.AnyAsync(x => x.SourceCartId == other.CartId));
        }
        var detail = await c.Inventory.GetAsync();
        await c.Source.Client.JsonAsync(HttpMethod.Post, c.Inventory.Path("cancel"),
            new DeliveryInventoryCancelRequest(Guid.NewGuid(), detail.Version, "Khách hủy trước khi xuất kho"));
        await using (var verify = c.Source.Context())
        {
            var voucher = await verify.CustomerRewardVouchers.SingleAsync(x => x.Id == c.VoucherId);
            Assert.Equal(CustomerRewardVoucherStatus.Available, voucher.Status); Assert.Null(voucher.UsedAtUtc); Assert.Null(voucher.UsedOrderId);
        }
        await DeliveryPriceStockCase.AssignVoucherAsync(other, c.CustomerId!.Value, c.VoucherId!.Value);
        var second = await DeliveryD03Support.Create(other);
        Assert.Equal(27000, second.Delivery.QuotedTotal);
        await AssertReservedVoucher(c, other.CartId);
    }

    [Theory]
    [InlineData("promotion")]
    [InlineData("voucher")]
    public async Task P04_Identity_change_without_cart_rowversion_change_invalidates_the_original_fingerprint_without_effects(string kind)
    {
        using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, kind == "promotion" ? "line" : "none");
        if (kind == "voucher") await c.ApplyVoucherAsync();
        var request = await DeliveryD03Support.Request(c.Source);
        await using (var db = c.Source.Context())
        {
            if (kind == "promotion")
            {
                var old = await db.Promotions.AsNoTracking().SingleAsync(x => x.Id == c.PromotionId);
                var replacement = new Promotion { StoreId = old.StoreId, Name = old.Name + " replacement", Type = old.Type,
                    DiscountType = old.DiscountType, DiscountValue = old.DiscountValue,
                    StartAtUtc = old.StartAtUtc, EndAtUtc = old.EndAtUtc, IsActive = true };
                db.Promotions.Add(replacement); await db.SaveChangesAsync();
                await db.OrderLines.Where(x => x.OrderId == c.Source.CartId && !x.IsDeleted).ExecuteUpdateAsync(s => s.SetProperty(x => x.PromotionId, replacement.Id));
            }
            else
            {
                var replacement = new CustomerRewardVoucher { StoreId = c.Source.Account.Store.StoreId, CustomerId = c.CustomerId!.Value,
                    VoucherCode = "COMP-" + Guid.NewGuid().ToString("N"), Value = 3000, RequiredAmount = 0,
                    IssuedAtUtc = DateTime.UtcNow, Status = CustomerRewardVoucherStatus.Available };
                db.CustomerRewardVouchers.Add(replacement); await db.SaveChangesAsync();
                await db.Set<OrderRewardVoucher>().Where(x => x.OrderId == c.Source.CartId).ExecuteUpdateAsync(s => s.SetProperty(x => x.VoucherId, replacement.Id));
            }
            var current = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == c.Source.CartId);
            Assert.Equal(request.ExpectedVersion, Convert.ToBase64String(current.RowVersion));
        }
        await DeliveryD03Support.Reject(c.Source, request, HttpStatusCode.Conflict, "CART_CHANGED");
        await using var verify = c.Source.Context();
        Assert.Empty(await verify.OrderPayments.Where(x => x.OrderId == c.Source.CartId).ToArrayAsync());
        Assert.False(await verify.DeliveryCommandReceipts.AnyAsync(x => x.ClientRequestId == request.ClientRequestId));
    }

    [Fact]
    public async Task S01_Empty_and_partial_FIFO_stock_can_be_physically_held_dispatched_and_returned_once_with_genuine_provisional_cost()
    {
        foreach (var initialStock in new[] { 0m, 1m })
        {
            using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, stock: initialStock);
            var estimatedUnitCost = initialStock == 0 ? 0m : 5000m;
            var dispatchedCost = initialStock * 5000m + (3m - initialStock) * estimatedUnitCost;
            await c.CreateAsync(); var ready = await c.ReadyAsync();
            await using (var held = c.Source.Context())
            {
                var balance = await held.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.FirstVariant);
                Assert.Equal(initialStock, balance.OnHandQty); Assert.Equal(3m, balance.ReservedQty);
                Assert.Equal(3m, (await held.InventoryReservations.SingleAsync(x => x.DeliveryOrderId == c.Source.Detail.Id &&
                    x.Status == InventoryReservationStatus.Active)).ReservedQty);
            }
            var finance = await c.Inventory.FinanceAsync();
            var request = new DeliveryInventoryDispatchRequest(Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
            var ack = await c.Inventory.DispatchAsync(request);
            var after = await c.SnapshotAsync(); var replay = await c.Inventory.DispatchAsync(request);
            Assert.False(ack.Replayed); Assert.True(replay.Replayed); Assert.Equal(ack.JournalEntryIds, replay.JournalEntryIds);
            Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(finance, await c.Inventory.FinanceAsync());
            await using (var db = c.Source.Context())
            {
                var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.FirstVariant);
                Assert.Equal(initialStock - 3, balance.OnHandQty); Assert.Equal(0m, balance.ReservedQty);
                var journal = Assert.Single(await db.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync());
                Assert.Equal(3m, journal.BaseQuantity); Assert.Equal(dispatchedCost, journal.CostAmount); Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
                var parts = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync();
                Assert.Equal(3m, parts.Sum(x => x.BaseQuantity)); Assert.Equal(dispatchedCost, parts.Sum(x => x.CostAmount));
                var values = await db.InventoryValuationEntries.Where(x => parts.Select(p => p.InventoryValuationEntryId).Contains(x.Id)).ToArrayAsync();
                var provisional = Assert.Single(values, x => x.IsProvisional);
                Assert.Null(provisional.CostFinalizedAtUtc); Assert.Equal(-(3m - initialStock), provisional.Quantity);
                Assert.Equal(initialStock == 0 ? InventoryCostSourceType.Unknown : InventoryCostSourceType.LastInboundCost,
                    provisional.CostSourceType);
                var unknownPart = Assert.Single(parts, x => x.InventoryValuationEntryId == provisional.Id);
                Assert.Null(unknownPart.InventoryCostLayerId); Assert.Null(unknownPart.InventoryCostBudgetDebitId);
                Assert.Equal(estimatedUnitCost, unknownPart.UnitCost);
                Assert.True(unknownPart.InventoryCostLayerAllocationId > 0);
                var movement = await db.InventoryTransactions.SingleAsync(x => x.Id == journal.InventoryTransactionId);
                Assert.Equal(initialStock, movement.BeforeQty); Assert.Equal(-3m, movement.QuantityChange); Assert.Equal(initialStock - 3m, movement.AfterQty);
                Assert.Equal(c.Source.Account.Store.WarehouseId, movement.WarehouseId);
                Assert.Equal(InventoryReferenceType.Delivery, movement.ReferenceType); Assert.True(movement.IsProvisionalCost);
            }
            var inventory = await c.Inventory.GetAsync();
            Assert.Contains(inventory.CostFragments, x => x.CostQuality == (initialStock == 0 ? "Unknown" : "Provisional"));
            var (_, quantityViewer) = await c.Picking.ActorAsync(PermissionCodes.Delivery.View);
            using (quantityViewer)
            {
                var publicSummary = (await quantityViewer.Http.GetFromJsonAsync<DeliveryInventorySummaryDto>(c.Inventory.Path()))!;
                Assert.False(publicSummary.CanViewCosts); Assert.Empty(publicSummary.CostFragments);
                Assert.All(publicSummary.Lines, x => { Assert.Null(x.DispatchedCostText); Assert.Null(x.AcceptedReturnedCostText); });
            }
            var line = Assert.Single((await c.Picking.GetAsync()).Lines);
            var returnRequest = new DeliveryInventoryReceiveRequest(Guid.NewGuid(), (await c.Picking.GetAsync()).Delivery.Version,
                [new(line.LineId, "1")], "Nhận lại một hàng đã xuất tạm tính");
            await c.Inventory.ReceiveAsync("1", returnRequest);
            var returned = await c.SnapshotAsync();
            Assert.True((await c.Inventory.ReceiveAsync("1", returnRequest)).Replayed);
            Assert.Equal(returned, await c.SnapshotAsync());
            await using var verify = c.Source.Context();
            Assert.Equal(initialStock - 2m, (await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.FirstVariant)).OnHandQty);
            var receipt = Assert.Single(await verify.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync());
            Assert.Equal(1m, receipt.BaseQuantity); Assert.Equal(estimatedUnitCost, receipt.CostAmount); Assert.True(receipt.IsProvisional);
            Assert.Equal(finance, await c.Inventory.FinanceAsync());
        }
    }

    [Fact]
    public async Task S02_An_unresolved_return_then_different_cost_receipt_resolves_only_the_outside_budget_and_later_return_uses_actual_cost()
    {
        foreach (var returnBeforeResolution in new[] { true, false })
        {
            using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, stock: 0);
            // Establish an actual last-inbound cost, then exhaust stock. Catalog CostPrice
            // alone is not warehouse cost evidence; a first-ever unknown dispatch stays Unknown.
            await using (var history = c.Source.Context())
            {
                var movement = InventoryPosPostingContractTests.CreateRealMovementService(history);
                var factory = new InventoryMovementFactory();
                await movement.CreateAsync(factory.CreatePurchaseReceipt(c.Source.Account.Store.WarehouseId, c.FirstVariant,
                    1, 5000, "COMP-HISTORY-" + c.Source.CartId, 1, "Historical inbound cost", 1));
                await movement.CreateAsync(factory.CreateAdjustmentDecrease(c.Source.Account.Store.WarehouseId, c.FirstVariant,
                    1, null, "Exhaust historical stock before provisional dispatch"));
            }
            await c.CreateAsync(); await c.ReadyAsync(); await c.Inventory.DispatchAsync();
            var finance = await c.Inventory.FinanceAsync();
            if (returnBeforeResolution) await c.Inventory.ReceiveAsync("1");
            await using (var db = c.Source.Context())
                await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                    new InventoryMovementFactory().CreatePurchaseReceipt(c.Source.Account.Store.WarehouseId, c.FirstVariant,
                        4m, 7000m, "COMP-RESOLVE-" + c.Source.CartId, 1, "Genuine later receipt", 1));
            if (!returnBeforeResolution) await c.Inventory.ReceiveAsync("1");
            var residual = Assert.Single(await c.Inventory.ResidualAsync("1"));
            Assert.Equal("7000", residual.DeliveredCostText);
            Assert.Equal(returnBeforeResolution ? "5000" : "7000", residual.AcceptedReturnedCostText);
            var summary = await c.Inventory.GetAsync(); Assert.Contains(summary.CostFragments, x => x.CostQuality == "Resolved");
            await c.Inventory.ReceiveAsync("1");
            await using var verify = c.Source.Context();
            var original = Assert.Single(await verify.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync());
            Assert.Equal(3m, original.BaseQuantity); Assert.Equal(5000m, original.UnitCost); Assert.Equal(15000m, original.CostAmount);
            Assert.True(original.IsProvisional); Assert.Null(original.InventoryCostLayerId); Assert.Null(original.InventoryCostBudgetDebitId);
            var returns = await verify.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).OrderBy(x => x.Id).ToArrayAsync();
            Assert.Equal(2, returns.Length);
            Assert.Equal(new[] { returnBeforeResolution ? 5000m : 7000m, 7000m }, returns.Select(x => x.CostAmount));
            Assert.Equal(2m, returns.Sum(x => x.BaseQuantity));
            Assert.All(returns, x => Assert.Equal(original.Id, x.DispatchCostFragmentId));
            Assert.Equal(new[] { 2m, 1m }, returns.Select(x => x.StartQuantity));
            Assert.Equal(new[] { 3m, 2m }, returns.Select(x => x.EndQuantity));
            Assert.Equal(new[] { returnBeforeResolution, false }, returns.Select(x => x.IsProvisional));
            var reversals = await verify.InventoryCostLayerAllocations.AsNoTracking()
                .Where(x => x.ReverseOfAllocationId == original.InventoryCostLayerAllocationId).ToArrayAsync();
            Assert.Equal(returnBeforeResolution ? 1 : 0, reversals.Length);
            if (returnBeforeResolution)
            {
                var reversal = Assert.Single(reversals);
                Assert.Equal(1m, reversal.Quantity); Assert.Equal(5000m, reversal.Amount);
                Assert.True(reversal.IsProvisional); Assert.Null(reversal.InventoryCostLayerId);
            }
            var resolutions = await verify.InventoryCostBudgetDebits.AsNoTracking()
                .Where(x => x.InventoryCostLayerAllocationId == original.InventoryCostLayerAllocationId && x.IsProvisionalResolution).ToArrayAsync();
            Assert.Equal(returnBeforeResolution ? 2m : 3m, resolutions.Sum(x => x.ResolutionEndQuantity - x.ResolutionStartQuantity));
            Assert.Equal(returnBeforeResolution ? 14000m : 21000m, resolutions.Sum(x => x.Amount));
            var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.FirstVariant);
            Assert.Equal(3m, balance.OnHandQty); Assert.Equal(21000m, balance.InventoryValue);
            Assert.Equal(finance, await c.Inventory.FinanceAsync());
        }
    }

    [Fact]
    public async Task S03_Negative_dispatch_failure_rolls_back_estimates_holds_and_receipts_before_an_exact_once_retry()
    {
        using var c = await DeliveryPriceStockCase.PrepareAsync(fixture, stock: 0);
        await c.CreateAsync(); var ready = await c.ReadyAsync();
        var request = new DeliveryInventoryDispatchRequest(Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
        var before = await c.SnapshotAsync();
        await Assert.ThrowsAsync<D05InjectedFailure>(() => c.Inventory.DispatchAsync(request, new D05AfterStockFailure()));
        Assert.Equal(before, await c.SnapshotAsync());
        await using (var db = c.Source.Context()) Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ProductVariantId == c.FirstVariant && x.QuantityChange < 0));
        Assert.False((await c.Inventory.DispatchAsync(request)).Replayed);
        Assert.True((await c.Inventory.DispatchAsync(request)).Replayed);
        await using var verify = c.Source.Context();
        Assert.Equal(-3m, (await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == c.FirstVariant)).OnHandQty);
        Assert.Single(await verify.DeliveryJournalEntries.Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync());
        Assert.Single(await verify.InventoryTransactions.Where(x => x.ProductVariantId == c.FirstVariant && x.QuantityChange < 0).ToArrayAsync());
    }

    [Fact]
    public async Task M01_Current74_matches_the_complete_manifest_and_provisional_evidence_refuses_unsafe_Down_before_DDL()
    {
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryPriceStockCase.PrepareAsync(isolated, stock: 0);
            await c.CreateAsync(); await c.ReadyAsync(); await c.Inventory.DispatchAsync(); await c.Inventory.ReceiveAsync("1");
            await using var db = c.Source.Context();
            async Task AssertCurrent()
            {
                var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
                Assert.Equal(80, applied.Length); Assert.Equal("20261022090000_AddDurableAuditOutbox", applied[^1]);
                Assert.Equal("20261008100000_AddDeliveryTrips", applied[72]);
                var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
                Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(applied, out var expected));
                await db.Database.OpenConnectionAsync();
                try
                {
                    var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied);
                    var compared = DatabaseSchemaComparer.Compare(expected, actual);
                    Assert.True(compared.IsMatch, JsonSerializer.Serialize(compared)); Assert.Equal(0, compared.Mismatches.Total);
                    Assert.Equal(41, actual.Tables.Sum(x => x.Triggers.Count));
                    var fragments = Assert.Single(actual.Tables, x => x.Identity.Name == "deliverydispatchcostfragments");
                    Assert.True(Assert.Single(fragments.Columns, x => x.Name == "inventorycostlayerid").IsNullable);
                    Assert.True(Assert.Single(fragments.Columns, x => x.Name == "inventorycostbudgetdebitid").IsNullable);
                    var preflight = new SqlServerDatabaseBaselinePreflight(db, new EfCoreDatabaseMigrationCatalog(db),
                        new SqlServerDatabaseObjectInventoryReader(db), catalog, new SqlServerSchemaSnapshotReader(db));
                    Assert.True((await preflight.InspectAsync()).IsAllowed);
                }
                finally { await db.Database.CloseConnectionAsync(); }
                Assert.False(db.Database.HasPendingModelChanges());
            }
            await AssertCurrent(); var before = await c.SnapshotAsync();
            var original = Assert.Single(await db.DeliveryDispatchCostFragments.AsNoTracking()
                .Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync());
            Assert.True(original.IsProvisional);
            foreach (var (statement, id) in new[] {
                ("UPDATE InventoryCostLayerAllocations SET Amount=Amount+1 WHERE Id=@id", original.InventoryCostLayerAllocationId),
                ("UPDATE InventoryTransactions SET TotalCost=TotalCost+1 WHERE Id=@id", original.InventoryTransactionId),
                ("UPDATE InventoryValuationEntries SET Amount=Amount+1 WHERE Id=@id", original.InventoryValuationEntryId) })
            {
                var corruption = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, new SqlParameter("@id", id)));
                Assert.Equal(51041, corruption.Number); Assert.Equal(before, await c.SnapshotAsync());
            }
            var proof = JsonSerializer.Serialize(new {
                Dispatch = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync(),
                Returns = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync() });
            await MigrationTestSupport.MigrateHistoricalPrefixAsync(isolated.Web.Database, "20261008120000_AddDeliveryProvisionalCosts");
            var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync("20261008100000_AddDeliveryTrips"));
            Assert.Equal(51040, error.Number); Assert.Equal(before, await c.SnapshotAsync());
            Assert.Equal(proof, JsonSerializer.Serialize(new {
                Dispatch = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync(),
                Returns = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Source.Detail.Id).ToArrayAsync() }));
            await db.Database.MigrateAsync();
            await AssertCurrent();
        }
        finally { await isolated.DisposeAsync(); }
    }

    private static async Task AssertReservedVoucher(DeliveryPriceStockCase c, int? sourceCartId = null)
    {
        await using var db = c.Source.Context();
        var voucher = await db.CustomerRewardVouchers.SingleAsync(x => x.Id == c.VoucherId);
        Assert.Equal(CustomerRewardVoucherStatus.ReservedForDelivery, voucher.Status);
        Assert.Null(voucher.UsedAtUtc); Assert.Null(voucher.UsedOrderId);
        var ownerCart = sourceCartId ?? c.Source.CartId;
        var owner = Assert.Single(await db.DeliveryOrders.Where(x => x.SourceCartId == ownerCart).ToArrayAsync());
        Assert.NotEqual(DeliveryState.Cancelled, owner.State);
    }
}
