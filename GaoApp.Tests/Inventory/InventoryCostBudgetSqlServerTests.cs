using System.Net.Http.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Entities;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class InventoryCostBudgetSqlServerTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task B01_Exact_four_digit_dispatch_and_accepted_return_can_be_consumed_by_central_sale_movement_legacy_step()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .0001m, stock: [(.001m, 10000m)]);
        await c.ReadyAsync("0.0001"); await c.DispatchAsync(); await c.ReceiveAsync("0.0001");
        var variant = await c.VariantAsync();
        await using var db = c.Picking.Source.Context();
        var movement = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
            new InventoryMovementFactory().CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant,
                91501, 1, "D05 returned residual", .001m, 10000m));
        Assert.Equal(-.001m, movement.QuantityChange); Assert.Equal(-10m, movement.ValueChange);
        db.ChangeTracker.Clear();
        var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal((byte)1, balance.QuantityPrecisionVersion); Assert.Equal(0m, balance.OnHandQty); Assert.Equal(0m, balance.InventoryValue);
        var debits = await db.InventoryCostBudgetDebits.Where(x => x.ProductVariantId == variant && x.InventoryTransactionId == movement.InventoryTransactionId).ToArrayAsync();
        Assert.Equal(.001m, debits.Sum(x => x.EndQuantity - x.StartQuantity)); Assert.Equal(10m, debits.Sum(x => x.Amount));
        Assert.Contains(debits, x => x.EndQuantity - x.StartQuantity == .0001m && x.Amount == 1m);
    }

    [Fact]
    public async Task B02_Cumulative_midpoint_budget_preserves_positive_unit_cost_even_when_one_fragment_amount_is_zero()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .0001m, stock: []);
        await c.SeedHistoricalFinalSourceAsync(.0002m, .5m, .0001m);
        await c.ReadyAsync("0.0001"); await c.DispatchAsync(); await c.ReceiveAsync("0.0001");
        await using var db = c.Picking.Source.Context();
        var part = Assert.Single(await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(.0001m, part.BaseQuantity); Assert.Equal(.5m, part.UnitCost); Assert.Equal(.0001m, part.CostAmount);
        var returned = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(.5m, returned.UnitCost); Assert.Equal(.0001m, returned.CostAmount);
        var debit = await db.InventoryCostBudgetDebits.SingleAsync(x => x.Id == part.InventoryCostBudgetDebitId);
        Assert.Equal(.0002m, debit.AnchorQuantity); Assert.Equal(.0001m, debit.AnchorAmount);
        Assert.Equal(0m, debit.StartQuantity); Assert.Equal(.0001m, debit.EndQuantity);
        var layer = await db.InventoryCostLayers.SingleAsync(x => x.Id == part.InventoryCostLayerId);
        Assert.Equal(.0001m, layer.RemainingQuantity); Assert.Equal(0m, layer.RemainingCostAmount);
        Assert.Equal(debit.AnchorAmount, debit.Amount + layer.RemainingCostAmount);
        using var second = await DeliveryD05Case.CreateAsync(fixture, ordered: .0001m, stock: [], sharedVariant: await c.VariantAsync());
        await second.ReadyAsync("0.0001"); await second.DispatchAsync(); await second.ReceiveAsync("0.0001");
        db.ChangeTracker.Clear();
        var zeroPart = Assert.Single(await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == second.Id).ToArrayAsync());
        Assert.Equal(.0001m, zeroPart.BaseQuantity); Assert.Equal(.5m, zeroPart.UnitCost); Assert.Equal(0m, zeroPart.CostAmount);
        var zeroReturn = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == second.Id).ToArrayAsync());
        Assert.Equal(.5m, zeroReturn.UnitCost); Assert.Equal(0m, zeroReturn.CostAmount);
    }

    [Fact]
    public async Task B03_Known_Final_zero_source_dispatches_and_returns_zero_without_turning_into_unknown_cost()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: []);
        // A coherent persisted Final-zero inbound source is legitimate. The old
        // generic inbound factory still rejects zero and is not changed here.
        await c.SeedHistoricalFinalSourceAsync(8m, 0m, 0m);
        await c.ReadyAsync(); await c.DispatchAsync(); await c.ReceiveAsync("2");
        var summary = await c.GetAsync(); Assert.True(summary.CanViewCosts);
        Assert.Equal("0", Assert.Single(summary.Lines).DispatchedCostText);
        Assert.Equal("0", Assert.Single(summary.Lines).AcceptedReturnedCostText);
        Assert.All(summary.CostFragments, part => { Assert.Equal("Final", part.CostQuality); Assert.Equal("0", part.UnitCostText); });
        Assert.Equal("0", Assert.Single(await c.ResidualAsync("6")).DeliveredCostText);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("missing")]
    [InlineData("quantity")]
    [InlineData("warehouse")]
    [InlineData("provisional")]
    [InlineData("missing-final")]
    [InlineData("reference-subkey")]
    [InlineData("source-subkey")]
    [InlineData("same-cost")]
    public async Task B04_Historical_negative_resolution_leaves_authoritative_Final_remainder_or_rejects_missing_resolution_proof(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 2, stock: []);
        var variant = await c.VariantAsync(); var warehouse = c.Picking.Account.Store.WarehouseId;
        await using (var db = c.Picking.Source.Context())
        {
            var service = InventoryPosPostingContractTests.CreateRealMovementService(db); var factory = new InventoryMovementFactory();
            var negative = factory.CreateSaleFinalize(warehouse, variant, 92501, 1, "Historical provisional", 2m, 10m);
            negative.AllowNegativeBalance = true;
            await service.CreateAsync(negative);
            var sourceCost = fault == "same-cost" ? 10m : 12m;
            var inbound = await service.CreateAsync(factory.CreatePurchaseReceipt(warehouse, variant, 4m, sourceCost, "D05-HISTORICAL-" + c.Id, 1, "Final receipt", 1));
            db.ChangeTracker.Clear();
            var layer = await db.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(2m, layer.ResolvedProvisionalQty); Assert.Equal(2m, layer.RemainingQuantity);
            Assert.Equal((byte)0, layer.CostBudgetPolicy);
            Assert.Equal(sourceCost, layer.UnitCost);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(2m, balance.OnHandQty);
            Assert.Equal(fault == "same-cost" ? 20m : 32m, balance.InventoryValue);
            var sourceEntry = await db.InventoryValuationEntries.SingleAsync(x => x.ProductVariantId == variant && x.EntryType == InventoryValuationEntryType.Inbound);
            Assert.Equal(4m * sourceCost, sourceEntry.Amount);
            Assert.Equal(fault == "same-cost" ? 40m : 52m, inbound.ValueChange);
            if (fault == "same-cost")
                Assert.Empty(await db.InventoryValuationEntries.Where(x => x.ProductVariantId == variant && x.EntryType == InventoryValuationEntryType.Revaluation).ToArrayAsync());
            else
            {
                var resolution = await db.InventoryValuationEntries.SingleAsync(x => x.ProductVariantId == variant && x.EntryType == InventoryValuationEntryType.Revaluation);
                // Preserve the genuine legacy sign: -20 + 48 + 4 = 32,
                // although the authoritative remaining FIFO source is 24.
                Assert.Equal(4m, resolution.Amount);
                Assert.Equal(24m, layer.RemainingQuantity * layer.UnitCost);
                switch (fault)
                {
                    case "missing": resolution.IsDeleted = true; break;
                    case "quantity": resolution.Quantity = .0001m; break;
                    case "provisional": resolution.IsProvisional = true; break;
                    case "missing-final": resolution.CostFinalizedAtUtc = null; break;
                    case "reference-subkey": resolution.ReferenceSubKey = "D05-CORRUPT-SUBKEY"; break;
                    case "source-subkey": resolution.SourceReferenceSubKey = "D05-CORRUPT-SUBKEY"; break;
                    case "warehouse":
                        var other = new Warehouse { StoreId = c.Picking.Account.Store.StoreId,
                            LegalEntityId = c.Picking.Source.Detail.SourceLegalEntityId,
                            Code = "D05-HISTORY-" + Guid.NewGuid().ToString("N")[..12], Name = "D05 wrong historical warehouse" };
                        db.Warehouses.Add(other); await db.SaveChangesAsync();
                        // This controlled historical policy0 corruption belongs
                        // only to the GUID fixture. EF now treats scope as a key.
                        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", db.Database.GetDbConnection().Database);
                        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InventoryValuationEntries SET WarehouseId={other.Id} WHERE Id={resolution.Id}");
                        db.ChangeTracker.Clear();
                        Assert.Equal(other.Id, (await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == resolution.Id)).WarehouseId);
                        break;
                }
                await db.SaveChangesAsync();
            }
        }
        await c.ReadyAsync("2");
        if (fault != "same-cost")
        {
            var before = await c.SnapshotAsync();
            var failure = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
            Assert.Equal("COST_NOT_READY", failure.Code);
            var intendedDiagnostic = fault switch
            {
                "none" => "lượng/giá trị tồn dương không khớp toàn bộ nguồn FIFO đã xác định",
                "missing" or "quantity" or "warehouse" => "Lượng/giá trị bút toán nguồn không khớp chứng cứ nhập.",
                "reference-subkey" or "source-subkey" => "Định danh gốc của chuỗi bù âm lịch sử không khớp.",
                _ => "Chuỗi bù âm lịch sử không có nguồn duy nhất đã xác định."
            };
            Assert.Contains(intendedDiagnostic, failure.Message, StringComparison.Ordinal);
            Assert.Equal(before, await c.SnapshotAsync());
            return;
        }
        await c.DispatchAsync();
        await using var verify = c.Picking.Source.Context();
        var part = Assert.Single(await verify.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(2m, part.BaseQuantity); Assert.Equal(20m, part.CostAmount); Assert.Equal(10m, part.UnitCost);
        var promoted = await verify.InventoryCostLayers.SingleAsync(x => x.Id == part.InventoryCostLayerId);
        Assert.Equal(2m, promoted.BudgetAnchorQuantity); Assert.Equal(20m, promoted.BudgetAnchorAmount);
        Assert.Equal(0m, promoted.RemainingQuantity); Assert.Equal(0m, promoted.RemainingCostAmount);
        var depleted = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(0m, depleted.OnHandQty); Assert.Equal(0m, depleted.InventoryValue);
    }

    [Fact]
    public async Task B05_New_legacy_nonzero_quantity_rounding_to_zero_is_rejected_but_old_legacy_replay_remains_immutable()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: [(10m, 10m)]);
        var variant = await c.VariantAsync(); var factory = new InventoryMovementFactory();
        var legacy = factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 92502, 1, "Old legacy replay", .001m, 10m);
        int? originalId;
        await using (var db = c.Picking.Source.Context())
            originalId = (await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(legacy)).InventoryTransactionId;
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
        var before = await c.SnapshotAsync();
        await using (var db = c.Picking.Source.Context())
        {
            Assert.NotNull(originalId);
            var proof = await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == originalId);
            var persisted = System.Text.Json.JsonSerializer.Serialize(proof);
            var replay = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(legacy);
            Assert.False(replay.IsCreated); Assert.True(replay.IsSkipped); Assert.Null(replay.InventoryTransactionId);
            Assert.Equal(1, await db.InventoryTransactions.CountAsync(x => x.IdempotencyKey == proof.IdempotencyKey));
            Assert.Equal(persisted, System.Text.Json.JsonSerializer.Serialize(await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == originalId)));
            var invalid = factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 92503, 1, "Below legacy step", .0001m, 10m);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(invalid));
            Assert.Contains("BELOW_LEGACY_STEP", ex.Message, StringComparison.Ordinal);
        }
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("quantity")]
    [InlineData("unit")]
    public async Task B06_Corrupt_historical_source_chain_cannot_be_promoted_into_authoritative_cost(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .0001m, stock: []);
        await c.SeedHistoricalFinalSourceAsync(.0002m, .5m, .0001m);
        await using (var db = c.Picking.Source.Context())
        {
            var variant = await c.VariantAsync();
            var transaction = await db.InventoryTransactions.SingleAsync(x => x.ProductVariantId == variant);
            if (fault == "amount") transaction.TotalCost += .0001m;
            if (fault == "quantity") transaction.QuantityChange += .0001m;
            if (fault == "unit") transaction.UnitCostSnapshot += .000001m;
            await db.SaveChangesAsync();
        }
        await c.ReadyAsync("0.0001"); var before = await c.SnapshotAsync();
        var failure = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
        Assert.Equal("COST_NOT_READY", failure.Code); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(1, .0004)]
    [InlineData(.0005, .9994)]
    public async Task B07_Promoted_legacy_outbound_normalizes_its_delta_once_and_preserves_fourth_digit_balance_residual(decimal requested, decimal expected)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .0006m, stock: [(1.001m, 12.3457m)]);
        await c.ReadyAsync("0.0006"); await c.DispatchAsync(); var variant = await c.VariantAsync();
        await using var db = c.Picking.Source.Context();
        Assert.Equal(1.0004m, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant)).OnHandQty);
        var posted = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
            new InventoryMovementFactory().CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 94501, 1, "Precision residual", requested, 12.3457m));
        Assert.Equal(-decimal.Round(requested, 3, MidpointRounding.AwayFromZero), posted.QuantityChange);
        Assert.Equal(1.0004m, posted.BeforeQty); Assert.Equal(expected, posted.AfterQty);
        db.ChangeTracker.Clear(); Assert.Equal(expected, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant)).OnHandQty);
        var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == posted.InventoryTransactionId);
        Assert.Equal((byte)1, transaction.PostingPrecisionPolicy); Assert.Equal(expected, transaction.AfterQty);
    }

    [Fact]
    public async Task B08_Exact_small_FIFO_pieces_are_dispatched_with_conserved_amounts_and_source_units()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .001m, stock: []);
        await c.SeedHistoricalFinalSourceAsync(.0004m, 9m, .0036m);
        await c.SeedHistoricalFinalSourceAsync(.0006m, 11m, .0066m, append: true);
        await c.ReadyAsync("0.001"); await c.DispatchAsync();
        await using var db = c.Picking.Source.Context();
        var parts = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(2, parts.Length); Assert.Equal(new[] { .0004m, .0006m }, parts.Select(x => x.BaseQuantity));
        Assert.Equal(new[] { 9m, 11m }, parts.Select(x => x.UnitCost)); Assert.Equal(.0102m, parts.Sum(x => x.CostAmount));
        var variant = await c.VariantAsync(); var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(0m, balance.OnHandQty); Assert.Equal(0m, balance.InventoryValue);
    }

    [Fact]
    public async Task B09_Six_digit_source_unit_and_frozen_amount_conserve_delivered_prefix_and_received_tail()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: []);
        await c.SeedHistoricalFinalSourceAsync(1m, 12.345678m, 12.3457m);
        await c.ReadyAsync("1"); await c.DispatchAsync(); await c.ReceiveAsync("0.5");
        await using var db = c.Picking.Source.Context();
        var part = Assert.Single(await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(12.345678m, part.UnitCost); Assert.Equal(12.3457m, part.CostAmount);
        var returned = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
        Assert.Equal(12.345678m, returned.UnitCost); Assert.Equal(6.1728m, returned.CostAmount);
        var residual = Assert.Single(await c.ResidualAsync("0.5")); Assert.Equal("6.1729", residual.DeliveredCostText);
        Assert.Equal(part.CostAmount, DeliveryD05Case.Number(residual.DeliveredCostText) + returned.CostAmount);
    }

    [Fact]
    public async Task B10_Pretracked_legacy_balance_and_FIFO_refresh_after_delivery_and_again_in_a_second_transaction()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 2, stock: [(10m, 10m)]); var variant = await c.VariantAsync();
        await using var stale = c.Picking.Source.Context();
        var oldBalance = await stale.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        var oldLayer = await stale.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
        await stale.InventoryReservations.Where(x => x.ProductVariantId == variant).LoadAsync();
        await stale.InventoryCostLayerAllocations.Where(x => x.InventoryCostLayerId == oldLayer.Id).LoadAsync();
        Assert.Equal((byte)0, oldBalance.QuantityPrecisionVersion); Assert.Equal((byte)0, oldLayer.CostBudgetPolicy);
        Assert.Equal(10m, oldBalance.OnHandQty); Assert.Equal(10m, oldLayer.RemainingQuantity);
        await c.ReadyAsync("2"); await c.DispatchAsync();
        var service = InventoryPosPostingContractTests.CreateRealMovementService(stale); var factory = new InventoryMovementFactory();
        var first = await service.CreateAsync(factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 95501, 1, "After stale tracked source", 1m, 10m));
        Assert.Equal(8m, first.BeforeQty); Assert.Equal(7m, first.AfterQty);
        await using (var writer = c.Picking.Source.Context())
            await InventoryPosPostingContractTests.CreateRealMovementService(writer).CreateAsync(
                factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 95502, 1, "Separate transaction", 1m, 10m));
        var second = await service.CreateAsync(factory.CreateSaleFinalize(c.Picking.Account.Store.WarehouseId, variant, 95503, 1, "Same context fresh next transaction", 1m, 10m));
        Assert.Equal(6m, second.BeforeQty); Assert.Equal(5m, second.AfterQty);
        await using var verify = c.Picking.Source.Context();
        var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        var layer = await verify.InventoryCostLayers.SingleAsync(x => x.Id == oldLayer.Id);
        Assert.Equal((byte)1, balance.QuantityPrecisionVersion); Assert.Equal(5m, balance.OnHandQty); Assert.Equal(50m, balance.InventoryValue);
        Assert.Equal(5m, layer.RemainingQuantity); Assert.Equal(50m, layer.RemainingCostAmount);
        var intervals = await verify.InventoryCostBudgetDebits.Where(x => x.InventoryCostLayerId == layer.Id).OrderBy(x => x.StartQuantity).ToArrayAsync();
        Assert.Equal(new[] { 0m, 2m, 3m, 4m }, intervals.Select(x => x.StartQuantity));
        Assert.Equal(new[] { 2m, 3m, 4m, 5m }, intervals.Select(x => x.EndQuantity)); Assert.Equal(50m, intervals.Sum(x => x.Amount));
    }

    [Fact]
    public async Task B11_Authenticated_POS_cash_sale_consumes_positive_unit_zero_amount_return_and_reports_Final_cost_once()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: .0002m, stock: []);
        var variant = await c.VariantAsync();
        await using (var pricing = c.Picking.Source.Context())
        {
            // Normal POS rounds revenue to whole dong. Price this case-owned
            // retail catalog/conversion so a .001 sale has a real cash total10.
            var catalog = await pricing.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == variant);
            catalog.Price = 10000m;
            var conversion = await pricing.ProductUnitConversions.SingleOrDefaultAsync(x => x.ProductVariantId == variant && x.UnitId == catalog.Product.BaseUnitId);
            if (conversion is null)
            {
                conversion = new ProductUnitConversion { StoreId = catalog.StoreId, ProductVariantId = variant,
                    UnitId = catalog.Product.BaseUnitId, Factor = 1m, Price = 10000m,
                    IsBaseUnit = true, IsDefaultForSale = true, IsActive = true };
                pricing.ProductUnitConversions.Add(conversion);
            }
            Assert.Equal(1m, conversion.Factor); conversion.Price = 10000m;
            await pricing.SaveChangesAsync();
        }
        // Both complete historical Final graphs precede promotion. The ordinary
        // legacy inbound writer still normalizes to three quantity digits.
        await c.SeedHistoricalFinalSourceAsync(.0002m, .5m, .0001m);
        await c.SeedHistoricalFinalSourceAsync(.0009m, 10000m, 9m, append: true);
        await c.ReadyAsync("0.0002"); await c.DispatchAsync(); await c.ReceiveAsync("0.0001");
        int returnedLayerId;
        await using (var db = c.Picking.Source.Context())
        {
            var returned = Assert.Single(await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(.0001m, returned.BaseQuantity); Assert.Equal(.5m, returned.UnitCost); Assert.Equal(0m, returned.CostAmount);
            var layer = await db.InventoryCostLayers.SingleAsync(x => x.InventoryValuationEntryId == returned.InventoryValuationEntryId);
            returnedLayerId = layer.Id; Assert.Equal(.5m, layer.UnitCost);
            Assert.Equal(.0001m, layer.RemainingQuantity); Assert.Equal(0m, layer.RemainingCostAmount);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(.001m, balance.OnHandQty); Assert.Equal(9m, balance.InventoryValue);
        }
        var sale = await c.FinalizePosCashSaleAsync(.001m);
        Assert.Equal(10m, sale.CashAmount);
        await using (var db = c.Picking.Source.Context())
        {
            var order = await db.Orders.Include(x => x.Lines).Include(x => x.Payments).SingleAsync(x => x.Id == sale.OrderId);
            Assert.Equal(OrderStatus.Completed, order.Status); Assert.Equal(sale.CashAmount, order.GrandTotal);
            Assert.Equal(sale.CashAmount, order.PaidTotal); Assert.Equal(0m, order.BalanceDue);
            var line = Assert.Single(order.Lines); Assert.Equal(variant, line.VariantId); Assert.Equal(.001m, line.BaseQuantity);
            Assert.False(line.IsProvisionalCost); Assert.Equal(9m, line.LineCostTotal); Assert.Equal(9000m, line.UnitCostSnapshot);
            var payment = Assert.Single(order.Payments); Assert.Equal(PaymentMethod.Cash, payment.Method); Assert.Equal(sale.CashAmount, payment.Amount);
            var shift = await db.POSShifts.SingleAsync(x => x.Id == order.POSShiftId);
            Assert.Equal(c.Picking.Account.Store.TerminalId, shift.TerminalId); Assert.Equal(sale.CashAmount, shift.CashSalesTotal);
            var movement = Assert.Single(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order &&
                x.ReferenceId == sale.OrderId.ToString() && x.ReferenceLineId == line.Id && x.TransactionType == InventoryTransactionType.SaleIssue).ToArrayAsync());
            Assert.Equal((byte)1, movement.PostingPrecisionPolicy); Assert.Equal(-.001m, movement.QuantityChange);
            Assert.Equal(-9m, movement.TotalCost); Assert.False(movement.IsProvisionalCost); Assert.NotNull(movement.CostFinalizedAtUtc);
            var debits = await db.InventoryCostBudgetDebits.Where(x => x.InventoryTransactionId == movement.Id).ToArrayAsync();
            Assert.Equal(2, debits.Length); Assert.Equal(.001m, debits.Sum(x => x.EndQuantity - x.StartQuantity)); Assert.Equal(9m, debits.Sum(x => x.Amount));
            var zero = Assert.Single(debits, x => x.InventoryCostLayerId == returnedLayerId);
            Assert.Equal(.0001m, zero.EndQuantity - zero.StartQuantity); Assert.Equal(.5m, zero.UnitCost); Assert.Equal(0m, zero.Amount);
            var final = Assert.Single(debits, x => x.InventoryCostLayerId != returnedLayerId);
            Assert.Equal(.0009m, final.EndQuantity - final.StartQuantity); Assert.Equal(10000m, final.UnitCost); Assert.Equal(9m, final.Amount);
            var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal(0m, balance.OnHandQty); Assert.Equal(0m, balance.InventoryValue); Assert.Equal(0m, balance.ReservedQty);
        }
        var report = (await c.Picking.Client.Http.GetFromJsonAsync<ProfitReportResponseDto>(
            "/admin/reports/profit/data?terminalId=" + c.Picking.Account.Store.TerminalId))!;
        Assert.Equal(ProfitQuality.Finalized, report.Current.Cogs.Quality); Assert.Equal(9m, report.Current.Cogs.Value);
        Assert.Equal(sale.CashAmount, report.Current.NetSales.Value); Assert.Equal(sale.CashAmount - 9m, report.Current.GrossProfit.Value);
        Assert.Contains(report.Details, x => x.OrderId == sale.OrderId);
        var after = await c.SnapshotAsync(); await c.ReplayPosCashSaleAsync(sale); Assert.Equal(after, await c.SnapshotAsync());
        await using var verify = c.Picking.Source.Context();
        Assert.Equal(sale.CashAmount, (await verify.POSShifts.SingleAsync(x => x.TerminalId == c.Picking.Account.Store.TerminalId)).CashSalesTotal);
        Assert.Single(await verify.OrderPayments.Where(x => x.OrderId == sale.OrderId).ToArrayAsync());
    }

    [Fact]
    public async Task B12_Fresh_manual_revaluation_on_promoted_balance_is_denied_before_any_effect()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: [(8m, 10000m)]);
        var variant = await c.VariantAsync();
        await c.ReadyAsync();
        await using (var db = c.Picking.Source.Context())
        {
            var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)1, balance.QuantityPrecisionVersion);
            Assert.Equal(8m, balance.OnHandQty); Assert.Equal(80000m, balance.InventoryValue); Assert.Equal(8m, balance.ReservedQty);
            var hold = await db.InventoryReservations.AsNoTracking().SingleAsync(x => x.DeliveryOrderId == c.Id);
            Assert.Equal(InventoryReservationStatus.Active, hold.Status); Assert.Equal(8m, hold.ReservedQty);
        }
        var before = await c.SnapshotAsync(); var sources = await CostSourceSnapshotAsync(c, variant);
        await using (var db = c.Picking.Source.Context())
        {
            var failure = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                InventoryPosPostingContractTests.CreateRealMovementService(db).CreateRevaluationAsync(
                    c.Picking.Account.Store.WarehouseId, variant, 8m, 12000m,
                    "D05-NEW-PROMOTED-REVALUE-" + c.Id, 1, "Fresh promoted revaluation"));
            Assert.Contains("SOURCE_BUDGET_REVALUATION_UNSUPPORTED", failure.Message, StringComparison.Ordinal);
        }
        Assert.Equal(before, await c.SnapshotAsync());
        Assert.Equal(sources, await CostSourceSnapshotAsync(c, variant));
    }

    [Fact]
    public async Task B13_Legitimate_legacy_manual_revaluation_value_gap_blocks_delivery_atomically_and_preserves_hold()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: [(8m, 10000m)]);
        var variant = await c.VariantAsync(); var reference = "D05-LEGACY-VALUE-GAP-" + c.Id;
        int originalId; string originalHistory;
        await using (var db = c.Picking.Source.Context())
        {
            var before = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)0, before.QuantityPrecisionVersion); Assert.Equal(8m, before.OnHandQty); Assert.Equal(80000m, before.InventoryValue);
            var result = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateRevaluationAsync(
                c.Picking.Account.Store.WarehouseId, variant, 8m, 12000m, reference, 1, "Legitimate legacy value adjustment");
            Assert.True(result.IsCreated); Assert.False(result.IsSkipped); Assert.NotNull(result.InventoryTransactionId);
            originalId = result.InventoryTransactionId.Value;
            Assert.Equal(0m, result.QuantityChange); Assert.Equal(80000m, result.BeforeValue);
            Assert.Equal(16000m, result.ValueChange); Assert.Equal(96000m, result.AfterValue);
            db.ChangeTracker.Clear();
            var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)0, balance.QuantityPrecisionVersion); Assert.Equal(8m, balance.OnHandQty); Assert.Equal(96000m, balance.InventoryValue);
            var layer = await db.InventoryCostLayers.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)0, layer.CostBudgetPolicy); Assert.Equal(8m, layer.RemainingQuantity); Assert.Equal(10000m, layer.UnitCost);
            Assert.Null(layer.BudgetAnchorAmount); Assert.Null(layer.RemainingCostAmount);
            var source = await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
            Assert.Equal(8m, source.Quantity); Assert.Equal(80000m, source.Amount); Assert.NotNull(source.CostFinalizedAtUtc);
        }
        originalHistory = await ManualRevaluationHistoryAsync(c, originalId);
        await c.ReadyAsync();
        await using (var db = c.Picking.Source.Context())
        {
            var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)1, balance.QuantityPrecisionVersion);
            Assert.Equal(8m, balance.OnHandQty); Assert.Equal(96000m, balance.InventoryValue); Assert.Equal(8m, balance.ReservedQty);
            var hold = await db.InventoryReservations.AsNoTracking().SingleAsync(x => x.DeliveryOrderId == c.Id);
            Assert.Equal(InventoryReservationStatus.Active, hold.Status); Assert.Equal(8m, hold.ReservedQty);
        }
        var held = await c.SnapshotAsync(); var sources = await CostSourceSnapshotAsync(c, variant);
        var failure = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
        Assert.Equal("COST_NOT_READY", failure.Code);
        Assert.Equal(held, await c.SnapshotAsync());
        Assert.Equal(sources, await CostSourceSnapshotAsync(c, variant));
        Assert.Equal(originalHistory, await ManualRevaluationHistoryAsync(c, originalId));
    }

    [Fact]
    public async Task B14_Completed_legacy_manual_revaluation_replays_immutably_after_balance_promotion()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: [(8m, 10000m)]);
        var variant = await c.VariantAsync(); var reference = "D05-LEGACY-REVALUE-REPLAY-" + c.Id;
        int originalId;
        await using (var db = c.Picking.Source.Context())
        {
            var created = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateRevaluationAsync(
                c.Picking.Account.Store.WarehouseId, variant, 8m, 12000m, reference, 1, "Completed historical revaluation");
            Assert.True(created.IsCreated); Assert.False(created.IsSkipped); Assert.NotNull(created.InventoryTransactionId);
            originalId = created.InventoryTransactionId.Value;
            Assert.Equal(8m, created.BeforeQty); Assert.Equal(8m, created.AfterQty);
            Assert.Equal(80000m, created.BeforeValue); Assert.Equal(16000m, created.ValueChange); Assert.Equal(96000m, created.AfterValue);
        }
        var originalHistory = await ManualRevaluationHistoryAsync(c, originalId);
        await c.ReadyAsync();
        var held = await c.SnapshotAsync(); var sources = await CostSourceSnapshotAsync(c, variant);
        await using (var db = c.Picking.Source.Context())
        {
            var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)1, balance.QuantityPrecisionVersion); Assert.Equal(8m, balance.ReservedQty);
            var replay = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateRevaluationAsync(
                c.Picking.Account.Store.WarehouseId, variant, 8m, 12000m, reference, 1, "Completed historical revaluation");
            Assert.False(replay.IsCreated); Assert.True(replay.IsSkipped); Assert.Equal(originalId, replay.InventoryTransactionId);
            Assert.Equal(0m, replay.QuantityChange); Assert.Equal(8m, replay.BeforeQty); Assert.Equal(8m, replay.AfterQty);
            Assert.Equal(80000m, replay.BeforeValue); Assert.Equal(16000m, replay.ValueChange); Assert.Equal(96000m, replay.AfterValue);
            Assert.Equal(1, await db.InventoryTransactions.CountAsync(x => x.ProductVariantId == variant &&
                x.ReferenceType == InventoryReferenceType.Adjustment && x.TransactionType == InventoryTransactionType.Revaluation &&
                x.ReferenceId == reference && x.ReferenceLineId == 1 && x.ReferenceSubKey == "COST-REVALUATION"));
            Assert.Single(await db.InventoryValuationEntries.AsNoTracking().Where(x => x.InventoryTransactionId == originalId).ToArrayAsync());
        }
        Assert.Equal(held, await c.SnapshotAsync());
        Assert.Equal(sources, await CostSourceSnapshotAsync(c, variant));
        Assert.Equal(originalHistory, await ManualRevaluationHistoryAsync(c, originalId));
    }

    [Theory]
    [InlineData("positive")]
    [InlineData("final-zero")]
    public async Task B15_Coherent_positive_and_Final_zero_sources_dispatch_to_zero_quantity_and_value(string source)
    {
        var cost = source == "positive" ? 10000m : 0m;
        using var c = await DeliveryD05Case.CreateAsync(fixture, stock: source == "positive" ? [(8m, cost)] : []);
        if (source == "final-zero") await c.SeedHistoricalFinalSourceAsync(8m, 0m, 0m);
        var variant = await c.VariantAsync();
        await c.ReadyAsync(); var finance = await c.FinanceAsync(); var ready = await c.Picking.GetAsync();
        var ack = await c.DispatchAsync(); Assert.False(ack.Replayed);
        await using (var db = c.Picking.Source.Context())
        {
            var balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)1, balance.QuantityPrecisionVersion);
            Assert.Equal(0m, balance.OnHandQty); Assert.Equal(0m, balance.InventoryValue); Assert.Equal(0m, balance.ReservedQty);
            var hold = await db.InventoryReservations.AsNoTracking().SingleAsync(x => x.DeliveryOrderId == c.Id);
            Assert.Equal(InventoryReservationStatus.Consumed, hold.Status); Assert.Equal(8m, hold.ReservedQty); Assert.NotNull(hold.ReleasedAtUtc);
            var part = Assert.Single(await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(8m, part.BaseQuantity); Assert.Equal(cost, part.UnitCost); Assert.Equal(8m * cost, part.CostAmount);
            var debit = await db.InventoryCostBudgetDebits.AsNoTracking().SingleAsync(x => x.Id == part.InventoryCostBudgetDebitId);
            Assert.Equal(8m, debit.EndQuantity - debit.StartQuantity); Assert.Equal(8m * cost, debit.Amount);
            var layer = await db.InventoryCostLayers.AsNoTracking().SingleAsync(x => x.Id == part.InventoryCostLayerId);
            Assert.Equal((byte)1, layer.CostBudgetPolicy); Assert.Equal(8m, layer.BudgetAnchorQuantity);
            Assert.Equal(8m * cost, layer.BudgetAnchorAmount); Assert.Equal(0m, layer.RemainingQuantity); Assert.Equal(0m, layer.RemainingCostAmount);
            Assert.Equal(layer.BudgetAnchorAmount, debit.Amount + layer.RemainingCostAmount);
            var sourceEntry = await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
            Assert.False(sourceEntry.IsProvisional); Assert.NotNull(sourceEntry.CostFinalizedAtUtc);
            Assert.NotEqual(InventoryCostSourceType.Unknown, sourceEntry.CostSourceType); Assert.Equal(cost, sourceEntry.UnitCost);
            var movement = await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == part.InventoryTransactionId);
            Assert.Equal((byte)2, movement.PostingPrecisionPolicy); Assert.Equal(-8m, movement.QuantityChange); Assert.Equal(-8m * cost, movement.TotalCost);
            Assert.False(movement.IsProvisionalCost); Assert.NotNull(movement.CostFinalizedAtUtc);
            var journal = Assert.Single(await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync());
            Assert.Equal(part.InventoryTransactionId, journal.InventoryTransactionId);
            Assert.Equal(8m, journal.BaseQuantity); Assert.Equal(8m * cost, journal.CostAmount); Assert.Equal(0m, journal.MoneyAmount);
            Assert.Equal(c.Picking.Source.Detail.SourceWarehouseId, journal.SourceWarehouseId);
            Assert.Equal(c.Picking.Source.Detail.SourceLegalEntityId, journal.SourceLegalEntityId);
            Assert.Equal(1, await db.DeliveryCommandReceipts.CountAsync(x => x.DeliveryOrderId == c.Id && x.ClientRequestId == ack.ClientRequestId));
        }
        var dispatched = await c.Picking.GetAsync();
        Assert.Equal(ready.Delivery.State, dispatched.Delivery.State); Assert.Equal(ready.Delivery.Revision + 1, dispatched.Delivery.Revision);
        var summary = await c.GetAsync(); Assert.True(summary.CanViewCosts);
        Assert.Equal("8", summary.DispatchedBaseQuantityText);
        Assert.All(summary.CostFragments, part => { Assert.Equal("Final", part.CostQuality); Assert.Equal(cost, DeliveryD05Case.Number(part.UnitCostText)); });
        Assert.Equal(finance, await c.FinanceAsync());
    }

    private static async Task<string> CostSourceSnapshotAsync(DeliveryD05Case c, int variant)
    {
        await using var db = c.Picking.Source.Context();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Balance = await db.InventoryBalances.AsNoTracking().SingleAsync(x => x.ProductVariantId == variant),
            Transactions = await db.InventoryTransactions.AsNoTracking().Where(x => x.ProductVariantId == variant).OrderBy(x => x.Id).ToArrayAsync(),
            Valuations = await db.InventoryValuationEntries.AsNoTracking().Where(x => x.ProductVariantId == variant).OrderBy(x => x.Id).ToArrayAsync(),
            Layers = await db.InventoryCostLayers.AsNoTracking().Where(x => x.ProductVariantId == variant).OrderBy(x => x.Id).ToArrayAsync(),
            Holds = await db.InventoryReservations.AsNoTracking().Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private static async Task<string> ManualRevaluationHistoryAsync(DeliveryD05Case c, int originalId)
    {
        await using var db = c.Picking.Source.Context();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Transaction = await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == originalId),
            Valuations = await db.InventoryValuationEntries.AsNoTracking().Where(x => x.InventoryTransactionId == originalId).OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}
