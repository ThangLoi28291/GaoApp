using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryLegacyOpeningReference")]
public sealed class DeliveryLegacyOpeningReferenceTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(12, 4, 7000)]
    [InlineData(250, 151, 2651)]
    public async Task Documented_legacy_opening_alias_preserves_consumed_source_history_and_dispatches_the_remaining_budget_once(
        int quantity, int consumed, int unitCost)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: []);
        var ids = await SeedLegacyOpeningAsync(c, quantity, unitCost);
        var priorId = await ConsumeLegacyAsync(c, consumed);
        await c.ReadyAsync("1");
        var history = await OriginalHistoryAsync(c, ids, priorId); var finance = await c.FinanceAsync();
        var ready = await c.Picking.GetAsync();
        var request = new DeliveryInventoryDispatchRequest(Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
        var posted = await c.DispatchAsync(request); Assert.False(posted.Replayed);
        var after = await c.SnapshotAsync(); var replay = await c.DispatchAsync(request);
        Assert.True(replay.Replayed); Assert.Equal(posted.JournalEntryIds, replay.JournalEntryIds);
        Assert.Equal(after, await c.SnapshotAsync()); Assert.Equal(history, await OriginalHistoryAsync(c, ids, priorId));
        Assert.Equal(finance, await c.FinanceAsync());
        await using var db = c.Picking.Source.Context(); var variant = await c.VariantAsync();
        decimal remainder = quantity - consumed, cost = unitCost;
        var balance = await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(remainder - 1, balance.OnHandQty); Assert.Equal((remainder - 1) * cost, balance.InventoryValue);
        Assert.Equal(0m, balance.ReservedQty);
        var layer = await db.InventoryCostLayers.SingleAsync(x => x.Id == ids.Layer);
        Assert.Equal((byte)1, layer.CostBudgetPolicy); Assert.Equal(remainder, layer.BudgetAnchorQuantity);
        Assert.Equal(remainder * cost, layer.BudgetAnchorAmount); Assert.Equal(remainder - 1, layer.RemainingQuantity);
        Assert.Equal((remainder - 1) * cost, layer.RemainingCostAmount);
        var fragment = await db.DeliveryDispatchCostFragments.SingleAsync(x => x.DeliveryOrderId == c.Id);
        Assert.Equal(ids.Layer, fragment.InventoryCostLayerId); Assert.True(fragment.InventoryCostBudgetDebitId > 0);
        Assert.Equal(1m, fragment.BaseQuantity); Assert.Equal(cost, fragment.UnitCost); Assert.Equal(cost, fragment.CostAmount);
        var value = await db.InventoryValuationEntries.SingleAsync(x => x.Id == fragment.InventoryValuationEntryId);
        Assert.False(value.IsProvisional); Assert.NotNull(value.CostFinalizedAtUtc);
        var debit = await db.InventoryCostBudgetDebits.SingleAsync(x => x.Id == fragment.InventoryCostBudgetDebitId);
        Assert.Equal(0m, debit.StartQuantity); Assert.Equal(1m, debit.EndQuantity); Assert.Equal(cost, debit.Amount);
        var journal = await db.DeliveryJournalEntries.SingleAsync(x => x.DeliveryOrderId == c.Id);
        Assert.Equal(1m, journal.BaseQuantity); Assert.Equal(cost, journal.CostAmount);
        Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
    }

    [Theory]
    [InlineData("arbitrary-alias")]
    [InlineData("alias-case")]
    [InlineData("non-legacy-reference")]
    [InlineData("missing-backlink")]
    [InlineData("wrong-backlink")]
    [InlineData("transaction-subkey")]
    [InlineData("transaction-line")]
    [InlineData("cost-snapshot")]
    public async Task A_legacy_label_does_not_admit_unproven_identity_or_cost_and_rejection_changes_no_facts(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: []);
        var ids = await SeedLegacyOpeningAsync(c, 12, 7000);
        int? otherLayer = null;
        if (fault == "wrong-backlink")
        {
            await c.SeedHistoricalFinalSourceAsync(1, 7000, 7000, append: true);
            await using var extra = c.Picking.Source.Context(); var variant = await c.VariantAsync();
            otherLayer = await extra.InventoryCostLayers.Where(x => x.ProductVariantId == variant && x.Id != ids.Layer).Select(x => x.Id).SingleAsync();
        }
        var priorId = await ConsumeLegacyAsync(c, 4); await c.ReadyAsync("1");
        await using (var db = c.Picking.Source.Context())
        {
            var source = await db.InventoryValuationEntries.SingleAsync(x => x.Id == ids.Valuation);
            var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == ids.Transaction);
            var layer = await db.InventoryCostLayers.SingleAsync(x => x.Id == ids.Layer);
            switch (fault)
            {
                case "arbitrary-alias": source.ReferenceSubKey = "OTHER-OPENING"; break;
                case "alias-case": source.ReferenceSubKey = "legacy-opening"; break;
                case "non-legacy-reference": source.ReferenceId = transaction.ReferenceId = layer.ReferenceId = "OTHER-PRODUCT"; break;
                case "missing-backlink": source.InventoryCostLayerId = null; break;
                case "wrong-backlink": source.InventoryCostLayerId = otherLayer; break;
                case "transaction-subkey": transaction.ReferenceSubKey = "OTHER-POS"; break;
                case "transaction-line": transaction.ReferenceLineId++; break;
                case "cost-snapshot": transaction.UnitCostSnapshot = 7001m; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault));
            }
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync(); var history = await OriginalHistoryAsync(c, ids, priorId);
        var error = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
        Assert.Equal("COST_NOT_READY", error.Code); Assert.Equal(before, await c.SnapshotAsync());
        Assert.Equal(history, await OriginalHistoryAsync(c, ids, priorId));
    }

    private static async Task<SourceIds> SeedLegacyOpeningAsync(DeliveryD05Case c, decimal quantity, decimal cost)
    {
        // Reproduce the documented initial-import graph only in the owned fixture;
        // the import uses different valuation and transaction/layer subkeys.
        await c.SeedHistoricalFinalSourceAsync(quantity, cost, quantity * cost);
        var variant = await c.VariantAsync(); await using var db = c.Picking.Source.Context();
        var layer = await db.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
        var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == layer.InventoryTransactionId);
        var source = await db.InventoryValuationEntries.SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
        transaction.TransactionType = InventoryTransactionType.OpeningBalance; source.EntryType = InventoryValuationEntryType.Opening;
        transaction.ReferenceType = source.ReferenceType = layer.ReferenceType = InventoryReferenceType.None;
        transaction.ReferenceId = source.ReferenceId = layer.ReferenceId = "LEGACY-PRODUCT";
        transaction.ReferenceLineId = source.ReferenceLineId = layer.ReferenceLineId = c.Id;
        transaction.ReferenceSubKey = layer.ReferenceSubKey = "LEGACY-POS"; source.ReferenceSubKey = "LEGACY-OPENING";
        source.InventoryCostLayerId = layer.Id;
        Assert.Equal(InventoryCostSourceType.Manual, source.CostSourceType);
        Assert.Equal(InventoryCostSourceType.Manual, transaction.CostSourceType); Assert.Equal((byte)0, transaction.PostingPrecisionPolicy);
        await db.SaveChangesAsync(); return new(transaction.Id, source.Id, layer.Id);
    }

    private static async Task<int> ConsumeLegacyAsync(DeliveryD05Case c, decimal quantity)
    {
        var variant = await c.VariantAsync(); await using var db = c.Picking.Source.Context();
        var request = new InventoryMovementFactory().CreateAdjustmentDecrease(c.Picking.Account.Store.WarehouseId, variant,
            quantity, null, "Prior legacy FIFO consumption");
        var result = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request);
        Assert.True(result.IsCreated); Assert.True(result.InventoryTransactionId > 0);
        var prior = await db.InventoryTransactions.SingleAsync(x => x.Id == result.InventoryTransactionId);
        return prior.Id;
    }

    private static async Task<string> OriginalHistoryAsync(DeliveryD05Case c, SourceIds ids, int priorId)
    {
        await using var db = c.Picking.Source.Context();
        return JsonSerializer.Serialize(new
        {
            Transactions = await db.InventoryTransactions.AsNoTracking().Where(x => x.Id == ids.Transaction || x.Id == priorId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.TransactionType, x.ReferenceType, x.ReferenceId, x.ReferenceLineId, x.ReferenceSubKey,
                    x.QuantityChange, x.UnitCostSnapshot, x.TotalCost, x.BeforeQty, x.AfterQty, x.BeforeInventoryValue, x.AfterInventoryValue,
                    x.CostSourceType, x.PostingPrecisionPolicy, x.IsProvisionalCost, x.CostFinalizedAtUtc }).ToArrayAsync(),
            Valuations = await db.InventoryValuationEntries.AsNoTracking().Where(x => x.InventoryTransactionId == ids.Transaction || x.InventoryTransactionId == priorId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.InventoryTransactionId, x.EntryType, x.ReferenceType, x.ReferenceId, x.ReferenceLineId, x.ReferenceSubKey,
                    x.InventoryCostLayerId, x.Quantity, x.UnitCost, x.Amount, x.CostSourceType, x.IsProvisional, x.CostFinalizedAtUtc }).ToArrayAsync(),
            Layer = await db.InventoryCostLayers.AsNoTracking().Where(x => x.Id == ids.Layer)
                .Select(x => new { x.Id, x.InventoryTransactionId, x.InventoryValuationEntryId, x.ReferenceType, x.ReferenceId, x.ReferenceLineId,
                    x.ReferenceSubKey, x.OriginalQuantity, x.UnitCost, x.IsProvisionalSource }).SingleAsync(),
            PriorAllocations = await db.InventoryCostLayerAllocations.AsNoTracking().Where(x => x.InventoryValuationEntry.InventoryTransactionId == priorId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.InventoryValuationEntryId, x.InventoryCostLayerId, x.Quantity, x.UnitCost, x.Amount,
                    x.IsProvisional, x.ResolvedQuantity, x.ResolvedAmount, x.IsResolved, x.ResolvedByInventoryCostLayerId, x.ReverseOfAllocationId }).ToArrayAsync()
        });
    }

    private sealed record SourceIds(int Transaction, int Valuation, int Layer);
}
