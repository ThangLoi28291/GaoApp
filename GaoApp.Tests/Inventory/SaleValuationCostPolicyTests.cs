using FluentAssertions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Inventory;

public sealed class SaleValuationCostPolicyTests
{
    [Theory]
    [InlineData(false, 12, 120)]
    [InlineData(false, 8, 80)]
    [InlineData(true, 12, 120)]
    [InlineData(true, 8, 80)]
    public void Both_writers_normalize_higher_and_lower_cost_and_full_void_closes(
        bool dedicated, decimal finalUnit, decimal expected)
    {
        var source = Source();
        var adjustment = Finalize(source, finalUnit, dedicated);
        var before = SaleValuationCostPolicy.Evaluate(source, [adjustment], []);
        before.State.Should().Be(SaleValuationCostPolicy.Quality.Finalized);
        before.Cost.Should().Be(expected);
        before.ProvisionalExposure.Should().Be(0);
        before.RequireFinalUnitCost().Should().Be(finalUnit);
        var reversal = Reverse(source, 10, finalUnit, isVoid: true);
        var closed = SaleValuationCostPolicy.Evaluate(source, [adjustment], [reversal]);
        closed.Cost.Should().Be(0);
        closed.InventoryReversedQuantity.Should().Be(10);
        closed.DisplayState.Should().Be("Đã xác định");
        source.Amount.Should().Be(-100, "the original audit row is immutable");
    }

    [Theory]
    [InlineData(false, 12, 96)]
    [InlineData(false, 8, 64)]
    [InlineData(true, 12, 96)]
    [InlineData(true, 8, 64)]
    public void Partial_restock_uses_normalized_final_cost(bool dedicated, decimal unit, decimal remaining)
    {
        var source = Source();
        var adjustment = Finalize(source, unit, dedicated);
        var result = SaleValuationCostPolicy.Evaluate(source, [adjustment], [Reverse(source, 2, unit)]);
        result.Cost.Should().Be(remaining);
        result.ProvisionalExposure.Should().Be(0);
        result.RequireFinalUnitCost().Should().Be(unit);
    }

    [Fact]
    public void Historical_wrong_sign_restock_and_void_are_conflicts_not_provisional()
    {
        var source = Source();
        var adjustment = Finalize(source, 12, dedicated: true);
        foreach (var qty in new[] { 2m, 10m })
        {
            var result = SaleValuationCostPolicy.Evaluate(source, [adjustment], [Reverse(source, qty, 8)]);
            result.State.Should().Be(SaleValuationCostPolicy.Quality.DataIntegrityConflict);
            result.Cost.Should().BeNull();
            result.DisplayState.Should().Be("Chưa đủ dữ liệu");
        }
    }

    [Fact]
    public void Multiple_revaluations_with_same_subkey_are_distinct_but_query_duplicates_are_not()
    {
        var source = Source();
        var first = Finalize(source, 12, false);
        first.Amount = 8;
        var second = Finalize(source, 13, false);
        second.Id = 202;
        second.Amount = 18;
        source.CostLayerAllocations.Single().ResolvedAmount = 126;
        var result = SaleValuationCostPolicy.Evaluate(source, [first, second, first], [first, second]);
        result.Cost.Should().Be(126);
        result.RequireFinalUnitCost().Should().Be(12.6m);
        SaleValuationCostPolicy.Evaluate(source, [first, second], [Reverse(source, 10, 12.6m, true)])
            .Cost.Should().Be(0);
    }

    [Fact]
    public void Repeated_returns_do_not_over_reverse_or_double_count()
    {
        var source = Source();
        var adjustment = Finalize(source, 12, false);
        var returns = new[] { Reverse(source, 2, 12), Reverse(source, 3, 12), Reverse(source, 5, 12) };
        for (var i = 0; i < returns.Length; i++) returns[i].Id += i;
        SaleValuationCostPolicy.Evaluate(source, [adjustment], returns.Take(1).ToArray()).Cost.Should().Be(96);
        SaleValuationCostPolicy.Evaluate(source, [adjustment], returns.Take(2).ToArray()).Cost.Should().Be(60);
        SaleValuationCostPolicy.Evaluate(source, [adjustment], returns).Cost.Should().Be(0);
        var extra = Reverse(source, 1, 12); extra.Id = 999;
        SaleValuationCostPolicy.Evaluate(source, [adjustment], [.. returns, extra]).State
            .Should().Be(SaleValuationCostPolicy.Quality.DataIntegrityConflict);
    }

    [Fact]
    public void Finalization_and_cost_quality_do_not_use_historical_flag_or_revaluation_count()
    {
        var source = Source();
        var ignoredZeroAdjustment = Finalize(source, 10, false);
        ignoredZeroAdjustment.Amount.Should().Be(0);
        var result = SaleValuationCostPolicy.Evaluate(source, [], []);
        result.State.Should().Be(SaleValuationCostPolicy.Quality.Finalized);
        result.ProvisionalExposure.Should().Be(0);
        source.IsProvisional.Should().BeTrue();
        result.DisplayState.Should().Be("Đã xác định");
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(4, 48, 60)]
    public void Provisional_exposure_has_an_explicit_quality_not_a_percentage(decimal resolved, decimal amount, decimal exposure)
    {
        var source = Source();
        var allocation = source.CostLayerAllocations.Single();
        allocation.ResolvedQuantity = resolved;
        allocation.ResolvedAmount = amount;
        var adjustments = new List<InventoryValuationEntry>();
        if (resolved > 0)
        {
            var adjustment = Finalize(source, 12, false);
            adjustment.Amount = 8;
            allocation.IsResolved = false;
            allocation.ResolvedQuantity = resolved;
            allocation.ResolvedAmount = amount;
            adjustments.Add(adjustment);
        }
        var result = SaleValuationCostPolicy.Evaluate(source, adjustments, []);
        result.Cost.Should().Be(100 + amount - resolved * 10);
        result.ProvisionalExposure.Should().Be(exposure);
        result.FinalUnitCost.Should().BeNull();
        result.DisplayState.Should().Be("Tạm tính");
        Action posting = () => result.RequireFinalUnitCost();
        posting.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Return_before_finalization_and_later_second_return_is_unavailable()
    {
        var source = Source();
        var adjustment = Finalize(source, 12, false);
        adjustment.Amount = 16;
        source.CostLayerAllocations.Single().ResolvedAmount = 116;
        var earlyReturn = Reverse(source, 2, 10);
        earlyReturn.OccurredAtUtc = source.OccurredAtUtc.AddSeconds(1);
        var result = SaleValuationCostPolicy.Evaluate(source, [adjustment], [earlyReturn]);
        result.State.Should().Be(SaleValuationCostPolicy.Quality.Unavailable);
        result.Cost.Should().BeNull();
        result.FinalUnitCost.Should().BeNull();
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("store")]
    [InlineData("warehouse")]
    [InlineData("variant")]
    [InlineData("writer")]
    [InlineData("missing-transaction")]
    [InlineData("warehouse-owner")]
    [InlineData("variant-owner")]
    public void Broken_or_ambiguous_history_never_becomes_zero_cost(string mutation)
    {
        var source = Source();
        var adjustment = Finalize(source, 12, false);
        switch (mutation)
        {
            case "parent": adjustment.RevaluationOfEntryId++; break;
            case "store": adjustment.StoreId++; break;
            case "warehouse": adjustment.WarehouseId++; break;
            case "variant": adjustment.ProductVariantId++; break;
            case "writer": adjustment.CostSourceType = InventoryCostSourceType.RevaluationAdjustment; break;
            case "missing-transaction": source.InventoryTransaction = null!; break;
            case "warehouse-owner": source.Warehouse.StoreId++; break;
            case "variant-owner": source.ProductVariant.StoreId++; break;
        }
        var result = SaleValuationCostPolicy.Evaluate(source, [adjustment], []);
        result.State.Should().BeOneOf(SaleValuationCostPolicy.Quality.Unavailable,
            SaleValuationCostPolicy.Quality.DataIntegrityConflict);
        result.Cost.Should().BeNull();
        result.ProvisionalExposure.Should().BeNull();
        result.DisplayState.Should().Be("Chưa đủ dữ liệu");
    }

    [Fact]
    public void Zero_cost_can_be_classified_but_does_not_enable_zero_cost_void_posting()
    {
        var source = Source(false);
        source.UnitCost = 0; source.Amount = 0;
        source.CostLayerAllocations.Single().UnitCost = 0;
        source.CostLayerAllocations.Single().Amount = 0;
        var result = SaleValuationCostPolicy.Evaluate(source, [], []);
        result.Cost.Should().Be(0);
        result.DisplayState.Should().Be("Đã xác định");
        Action posting = () => result.RequireFinalUnitCost();
        posting.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Final_cost_that_cannot_roundtrip_movement_precision_is_unavailable()
    {
        var source = Source();
        var adjustment = Finalize(source, 1.1m, false);
        source.Quantity = -1000000; source.UnitCost = 1; source.Amount = -1000000;
        source.InventoryTransaction.QuantityChange = source.Quantity;
        var allocation = source.CostLayerAllocations.Single();
        allocation.Quantity = 1000000; allocation.UnitCost = 1; allocation.Amount = 1000000;
        allocation.ResolvedQuantity = 1000000; allocation.ResolvedAmount = 1000000.0001m;
        adjustment.Amount = 0.0001m;
        var result = SaleValuationCostPolicy.Evaluate(source, [adjustment], []);
        result.State.Should().Be(SaleValuationCostPolicy.Quality.Unavailable);
        result.Cost.Should().BeNull();
        result.Reason.Should().Contain("precision");
    }

    internal static InventoryValuationEntry Source(bool provisional = true)
    {
        var time = new DateTime(2026, 9, 8, 1, 0, 0, DateTimeKind.Utc);
        return new InventoryValuationEntry
        {
            Id = 100, StoreId = 1, WarehouseId = 11, ProductVariantId = 99, InventoryTransactionId = 501,
            EntryType = InventoryValuationEntryType.Outbound, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "700", ReferenceLineId = 701, ReferenceSubKey = "SALE-SOURCE",
            Quantity = -10, UnitCost = 10, Amount = -100, IsProvisional = provisional,
            OccurredAtUtc = time, CostFinalizedAtUtc = provisional ? null : time,
            Warehouse = new Warehouse { Id = 11, StoreId = 1 },
            ProductVariant = new ProductVariant { Id = 99, StoreId = 1 },
            InventoryTransaction = new InventoryTransaction
            {
                Id = 501, StoreId = 1, WarehouseId = 11, ProductVariantId = 99,
                TransactionType = InventoryTransactionType.SaleIssue, QuantityChange = -10,
                ReferenceType = InventoryReferenceType.Order, ReferenceId = "700", ReferenceLineId = 701
            },
            CostLayerAllocations = new List<InventoryCostLayerAllocation>
            {
                new() { Id = 301, StoreId = 1, InventoryValuationEntryId = 100, Quantity = 10,
                    UnitCost = 10, Amount = 100, IsProvisional = provisional }
            }
        };
    }

    internal static InventoryValuationEntry Finalize(InventoryValuationEntry source, decimal unit, bool dedicated)
    {
        var time = source.OccurredAtUtc.AddMinutes(1);
        var allocation = source.CostLayerAllocations.Single();
        allocation.IsResolved = true; allocation.ResolvedAtUtc = time;
        allocation.ResolvedQuantity = dedicated ? 0 : 10;
        allocation.ResolvedAmount = dedicated ? 0 : unit * 10;
        allocation.Quantity = dedicated ? 0 : 10;
        if (dedicated) source.CostFinalizedAtUtc = time;
        return new InventoryValuationEntry
        {
            Id = 200, StoreId = 1, WarehouseId = 11, ProductVariantId = 99, InventoryTransactionId = 502,
            EntryType = InventoryValuationEntryType.Revaluation, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "700", ReferenceLineId = 701,
            ReferenceSubKey = dedicated ? "REVAL:L401:A301" : source.ReferenceSubKey,
            SourceReferenceSubKey = dedicated ? null : source.ReferenceSubKey,
            SourceValuationEntryId = dedicated ? null : source.Id, RevaluationOfEntryId = source.Id,
            CostSourceType = dedicated ? InventoryCostSourceType.RevaluationAdjustment : InventoryCostSourceType.Manual,
            UnitCost = unit, Amount = (unit - 10) * 10 * (dedicated ? -1 : 1),
            InventoryCostLayerId = 401, OccurredAtUtc = time,
            InventoryCostLayer = new InventoryCostLayer
            { Id = 401, StoreId = 1, WarehouseId = 11, ProductVariantId = 99, InventoryTransactionId = dedicated ? 503 : 502 },
            InventoryTransaction = new InventoryTransaction
            { Id = 502, StoreId = 1, WarehouseId = 11, ProductVariantId = 99, QuantityChange = dedicated ? 0 : 10 }
        };
    }

    internal static InventoryValuationEntry Reverse(InventoryValuationEntry source, decimal quantity, decimal unit, bool isVoid = false)
        => new()
        {
            Id = 600, StoreId = 1, WarehouseId = 11, ProductVariantId = 99, InventoryTransactionId = 601,
            EntryType = InventoryValuationEntryType.Inbound,
            ReferenceType = isVoid ? InventoryReferenceType.Order : InventoryReferenceType.Refund,
            ReferenceId = isVoid ? source.ReferenceId : "800", ReferenceLineId = isVoid ? source.ReferenceLineId : 801,
            SourceValuationEntryId = source.Id, SourceReferenceSubKey = source.ReferenceSubKey,
            Quantity = quantity, UnitCost = unit, Amount = Math.Round(quantity * unit, 4, MidpointRounding.AwayFromZero),
            OccurredAtUtc = source.OccurredAtUtc.AddMinutes(2),
            InventoryTransaction = new InventoryTransaction
            { Id = 601, StoreId = 1, WarehouseId = 11, ProductVariantId = 99,
                TransactionType = isVoid ? InventoryTransactionType.SaleVoidIn : InventoryTransactionType.CustomerReturnIn }
        };
}
