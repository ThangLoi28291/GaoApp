using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Reports;

public sealed class ProfitReportAggregationPolicyTests
{
    [Theory]
    [InlineData(200, 120, 80, 40)]
    [InlineData(-40, 60, -100, 250)]
    [InlineData(100, 130, -30, -30)]
    public void Signed_profit_and_margin_use_net_sales_without_absolute_denominator(
        decimal sales, decimal cost, decimal profit, decimal margin)
    {
        var result = ProfitReportAggregationPolicy.Summarize(sales, [new(cost, 0, ProfitQuality.Finalized, 0)], true);
        Assert.Equal(profit, result.GrossProfit.Value);
        Assert.Equal(margin, result.GrossMargin.Value);
    }

    [Fact]
    public void Empty_zero_and_unknown_are_three_different_outcomes()
    {
        var empty = ProfitReportAggregationPolicy.Summarize(0, [], false);
        Assert.True(empty.IsEmpty); Assert.Equal(0, empty.Cogs.Value); Assert.Null(empty.GrossMargin.Value);
        Assert.Equal("—", empty.CostState);
        var zero = ProfitReportAggregationPolicy.Summarize(0, [new(0, 0, ProfitQuality.Finalized, 0)], true);
        Assert.False(zero.IsEmpty); Assert.Equal("Đã xác định", zero.CostState);
        var unknown = ProfitReportAggregationPolicy.Summarize(200,
            [new(100, 0, ProfitQuality.Finalized, 0), new(null, null, ProfitQuality.Unavailable, null)], true);
        Assert.Equal(200, unknown.NetSales.Value);
        Assert.Null(unknown.Cogs.Value); Assert.Null(unknown.GrossProfit.Value); Assert.Null(unknown.GrossMargin.Value);
        Assert.Null(unknown.AffectedOrders);
    }

    [Fact]
    public void Tiny_and_zero_value_unresolved_cost_still_keeps_provisional_quality()
    {
        foreach (var exposure in new[] { 0m, 0.0001m, 60m })
        {
            var result = ProfitReportAggregationPolicy.Summarize(200,
                [new(108, exposure, ProfitQuality.Provisional, 1)], true);
            Assert.Equal("Tạm tính", result.CostState); Assert.Equal(exposure, result.ProvisionalCogs.Value);
            Assert.Equal(ProfitQuality.Provisional, result.GrossMargin.Quality);
            Assert.Equal(1, result.AffectedOrders); Assert.Equal(1, result.AffectedLines);
        }
    }

    [Theory]
    [InlineData(false, 12, 120)]
    [InlineData(false, 8, 80)]
    [InlineData(true, 12, 120)]
    [InlineData(true, 8, 80)]
    public void Real_policy_projection_normalizes_both_writer_signs(bool dedicated, decimal unit, decimal expected)
    {
        var s = Snapshot();
        Finalize(s, unit, dedicated);
        var p = Policy();
        var costs = p.EvaluateCosts(s);
        Assert.Equal(expected, costs[1].Cost);
        var activity = p.Activity(s, Query(), costs);
        Assert.Equal(expected - 100, activity.Net);
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("missing-transaction")]
    [InlineData("missing-line")]
    [InlineData("wrong-parent")]
    [InlineData("wrong-warehouse-store")]
    [InlineData("wrong-variant-store")]
    [InlineData("deleted-allocation")]
    [InlineData("wrong-reversal-cost")]
    [InlineData("void-residual")]
    [InlineData("missing-restock-mirror")]
    [InlineData("interleaved-return")]
    [InlineData("hybrid-writer")]
    [InlineData("dedicated-partial")]
    [InlineData("over-reversal")]
    public void Invalid_graph_never_becomes_clean_subset_cost(string mutation)
    {
        var s = Snapshot();
        switch (mutation)
        {
            case "missing-root": s.Entries.Clear(); break;
            case "missing-transaction": s.Entries[0].InventoryTransaction = null!; break;
            case "missing-line": s.Lines.Clear(); break;
            case "wrong-parent": Finalize(s, 12); s.Entries[1].RevaluationOfEntryId = 999; break;
            case "wrong-warehouse-store": s.Entries[0].Warehouse.StoreId = 2; break;
            case "wrong-variant-store": s.Entries[0].ProductVariant.StoreId = 2; break;
            case "deleted-allocation": s.Entries[0].CostLayerAllocations.Single().IsDeleted = true; break;
            case "wrong-reversal-cost": Return(s, 2, 9); break;
            case "void-residual": s.Orders[0].Status = OrderStatus.Voided; break;
            case "missing-restock-mirror": Return(s, 2); s.Entries.RemoveAt(1); break;
            case "interleaved-return":
                Finalize(s, 12); Return(s, 2, 10);
                s.Entries[^1].OccurredAtUtc = At.AddSeconds(1); break;
            case "hybrid-writer":
                Finalize(s, 12); s.Entries[1].CostSourceType = InventoryCostSourceType.RevaluationAdjustment; break;
            case "dedicated-partial":
                Finalize(s, 12, true); s.Entries[0].CostLayerAllocations.Single().IsResolved = false;
                s.Entries[0].CostLayerAllocations.Single().Quantity = 6;
                s.Entries[0].CostLayerAllocations.Single().ResolvedQuantity = 4;
                s.Entries[0].CostLayerAllocations.Single().ResolvedAmount = 48;
                s.Entries[1].Amount = -8; break;
            case "over-reversal": Return(s, 11); break;
        }
        var p = Policy(); var costs = p.EvaluateCosts(s);
        var result = p.Aggregate(s, Query(), costs).Summary;
        Assert.Null(result.Cogs.Value); Assert.Null(result.GrossProfit.Value); Assert.Null(result.GrossMargin.Value);
        Assert.NotNull(result.NetSales.Value);
    }

    [Fact]
    public void Fully_voided_root_is_validated_then_contributes_zero_revenue_and_cost()
    {
        var s = Snapshot(); Finalize(s, 12);
        var root = s.Entries[0];
        var mirror = Mirror(root, 10, 12);
        mirror.ReferenceType = InventoryReferenceType.Order; mirror.ReferenceId = "1"; mirror.ReferenceLineId = 1;
        mirror.InventoryTransaction.TransactionType = InventoryTransactionType.SaleVoidIn;
        s.Entries.Add(mirror); s.Orders[0].Status = OrderStatus.Voided;
        var p = Policy(); var result = p.Aggregate(s, Query(), p.EvaluateCosts(s)).Summary;
        Assert.Equal(0, result.Cogs.Value); Assert.Equal(0, result.NetSales.Value);
    }

    [Fact]
    public void NoRestock_keeps_cost_and_later_restock_reduces_only_actual_mirrored_cost()
    {
        var s = Snapshot(); Finalize(s, 12);
        Return(s, 2, 12, false);
        var p = Policy();
        Assert.Equal(120, p.EvaluateCosts(s)[1].Cost);
        Return(s, 2, 12);
        Assert.Equal(96, p.EvaluateCosts(s)[1].Cost);
    }

    [Fact]
    public void Return_only_period_keeps_revenue_event_date_and_cost_in_original_sale_period()
    {
        var s = Snapshot(); Return(s, 2);
        s.Returns[0].CompletedAtUtc = At.AddDays(2);
        var p = Policy();
        var cost = p.EvaluateCosts(s);
        var sale = p.Aggregate(s, Query(), cost).Summary;
        Assert.Equal(200, sale.NetSales.Value); Assert.Equal(80, sale.Cogs.Value);
        var later = p.Aggregate(s, Query(At.AddDays(2)), cost).Summary;
        Assert.Equal(-40, later.NetSales.Value); Assert.Equal(0, later.Cogs.Value);
        Assert.Equal(-40, later.GrossProfit.Value); Assert.Equal(100, later.GrossMargin.Value);
    }

    [Fact]
    public void Unknown_return_only_source_invalidates_cost_metrics_without_hiding_trustworthy_revenue()
    {
        var s = Snapshot(); Return(s, 2);
        s.Returns[0].CompletedAtUtc = At.AddDays(2); s.Entries.Clear();
        var p = Policy();
        var later = p.Aggregate(s, Query(At.AddDays(2)), p.EvaluateCosts(s)).Summary;
        Assert.Equal(-40, later.NetSales.Value); Assert.Null(later.Cogs.Value);
    }

    [Fact]
    public void Adjustments_after_sale_period_restate_primary_and_are_not_double_counted()
    {
        var s = Snapshot(); Finalize(s, 12);
        s.Entries[1].OccurredAtUtc = At.AddDays(2);
        s.Entries[0].CostLayerAllocations.Single().ResolvedAtUtc = At.AddDays(2);
        var p = Policy(); var cost = p.EvaluateCosts(s);
        Assert.Equal(120, p.Aggregate(s, Query(), cost).Summary.Cogs.Value);
        var activity = p.Activity(s, Query(At.AddDays(2)), cost);
        Assert.Equal(20, activity.Increase); Assert.Equal(0, activity.Decrease); Assert.Equal(1, activity.Count);
    }

    [Fact]
    public void Net_zero_activity_is_not_empty_and_counts_unique_durable_ids()
    {
        var s = Snapshot(); Finalize(s, 12);
        var negative = Mirror(s.Entries[0], 0, 8);
        negative.Id = 3; negative.EntryType = InventoryValuationEntryType.Revaluation;
        negative.CostSourceType = InventoryCostSourceType.Manual;
        negative.RevaluationOfEntryId = 1; negative.ReferenceType = InventoryReferenceType.Order;
        negative.ReferenceId = "1"; negative.ReferenceLineId = 1; negative.Amount = -20;
        negative.InventoryCostLayerId = s.Entries[1].InventoryCostLayerId;
        negative.InventoryCostLayer = s.Entries[1].InventoryCostLayer;
        negative.InventoryTransaction = s.Entries[1].InventoryTransaction;
        negative.InventoryTransactionId = s.Entries[1].InventoryTransactionId;
        negative.OccurredAtUtc = s.Entries[1].OccurredAtUtc;
        s.Entries.Add(negative); s.ActivityEntryIds.Add(3);
        s.Entries[0].CostLayerAllocations.Single().ResolvedAmount = 100;
        var p = Policy(); var activity = p.Activity(s, Query(), p.EvaluateCosts(s));
        Assert.Equal(20, activity.Increase); Assert.Equal(20, activity.Decrease);
        Assert.Equal(0, activity.Net); Assert.Equal(2, activity.Count); Assert.Equal(ProfitQuality.Finalized, activity.Quality);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(false, 12)]
    [InlineData(false, 8)]
    [InlineData(true, 12)]
    [InlineData(true, 8)]
    public void Multiple_returns_reconcile_each_report_generation_without_averaging(bool dedicated, decimal unit)
    {
        var s = Snapshot();
        if (unit != 10) Finalize(s, unit, dedicated);
        var remaining = 10m; var p = Policy();
        foreach (var quantity in new[] { 2m, 3m, 5m })
        {
            Return(s, quantity, unit); remaining -= quantity;
            var report = p.Aggregate(s, Query(), p.EvaluateCosts(s)).Summary;
            Assert.Equal(remaining * unit, report.Cogs.Value);
            Assert.Equal(remaining * 20, report.NetSales.Value);
            Assert.Equal(remaining * (20 - unit), report.GrossProfit.Value);
        }
        Return(s, 1, unit);
        Assert.Null(p.Aggregate(s, Query(), p.EvaluateCosts(s)).Summary.Cogs.Value);
    }

    [Fact]
    public void Actual_zero_cost_source_is_finalized_and_distinct_from_missing_source()
    {
        var s = Snapshot(); var root = s.Entries[0];
        root.UnitCost = 0; root.Amount = 0;
        var allocation = root.CostLayerAllocations.Single();
        allocation.UnitCost = 0; allocation.Amount = 0; allocation.ResolvedAmount = 0;
        var p = Policy(); var report = p.Aggregate(s, Query(), p.EvaluateCosts(s)).Summary;
        Assert.Equal(0, report.Cogs.Value); Assert.Equal(200, report.GrossProfit.Value);
        Assert.Equal(100, report.GrossMargin.Value); Assert.Equal("Đã xác định", report.CostState);
        Assert.False(report.IsEmpty);
    }

    [Fact]
    public void Return_terminal_controls_revenue_while_original_terminal_retains_restatement()
    {
        var s = Snapshot(); Return(s, 2);
        s.Shifts.Add(new POSShift { Id = 2, StoreId = 1, TerminalId = 2 });
        s.Returns[0].POSShiftId = 2;
        var p = Policy(); var cost = p.EvaluateCosts(s);
        var origin = Query(); origin.TerminalId = 1;
        var returned = Query(); returned.TerminalId = 2;
        var sale = p.Aggregate(s, origin, cost).Summary;
        var refund = p.Aggregate(s, returned, cost).Summary;
        Assert.Equal(200, sale.NetSales.Value); Assert.Equal(80, sale.Cogs.Value);
        Assert.Equal(-40, refund.NetSales.Value); Assert.Equal(0, refund.Cogs.Value);
        var all = p.Aggregate(s, Query(), cost).Summary;
        Assert.Equal(all.NetSales.Value, sale.NetSales.Value + refund.NetSales.Value);
        Assert.Equal(all.Cogs.Value, sale.Cogs.Value + refund.Cogs.Value);
    }

    internal static DateTime At = new(2026, 9, 8, 2, 0, 0, DateTimeKind.Utc);
    internal static ProfitReportAggregationPolicy Policy() => new(new SalesReportingPeriodPolicy());
    internal static SalesResolvedExecutiveQueryDto Query(DateTime? day = null) => new SalesReportingPeriodPolicy()
        .Resolve(new SalesExecutiveDashboardQueryDto { FromDate = (day ?? At).Date, ToDate = (day ?? At).Date, Compare = "none" }, At).Current;
    internal static ProfitSourceSnapshot Snapshot()
    {
        var tx = new InventoryTransaction { Id = 1, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            ReferenceType = InventoryReferenceType.Order, ReferenceId = "1", ReferenceLineId = 1,
            TransactionType = InventoryTransactionType.SaleIssue, QuantityChange = -10, OccurredAtUtc = At };
        var root = new InventoryValuationEntry { Id = 1, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            InventoryTransactionId = 1, InventoryTransaction = tx, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "1", ReferenceLineId = 1, EntryType = InventoryValuationEntryType.Outbound,
            Quantity = -10, UnitCost = 10, Amount = -100, OccurredAtUtc = At,
            Warehouse = new Warehouse { Id = 1, StoreId = 1 },
            ProductVariant = new ProductVariant { Id = 1, StoreId = 1 } };
        root.CostLayerAllocations.Add(new InventoryCostLayerAllocation { Id = 1, StoreId = 1,
            InventoryValuationEntryId = 1, Quantity = 10, UnitCost = 10, Amount = 100,
            IsResolved = true, ResolvedQuantity = 10, ResolvedAmount = 100, ResolvedAtUtc = At });
        return new ProfitSourceSnapshot { StoreId = 1, ReadAtUtc = At,
            Orders = [new Order { Id = 1, StoreId = 1, OrderNumber = "SALE-1", Status = OrderStatus.Completed,
                CompletedAtUtc = At, Subtotal = 200, POSShiftId = 1 }],
            Lines = [new OrderLine { Id = 1, StoreId = 1, OrderId = 1, VariantId = 1, ItemName = "Rice",
                Quantity = 10, BaseQuantity = 10 }],
            Shifts = [new POSShift { Id = 1, StoreId = 1, TerminalId = 1 }],
            Entries = [root] };
    }
    internal static void Finalize(ProfitSourceSnapshot s, decimal unit, bool dedicated = false)
    {
        var root = s.Entries[0]; root.IsProvisional = true; root.CostFinalizedAtUtc = At.AddMinutes(1);
        var a = root.CostLayerAllocations.Single();
        a.IsProvisional = true; a.IsResolved = true; a.ResolvedQuantity = 10; a.ResolvedAmount = unit * 10;
        a.ResolvedAtUtc = At.AddMinutes(1); if (dedicated) a.Quantity = 0;
        var tx = new InventoryTransaction { Id = 2, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            QuantityChange = dedicated ? 0 : 10, ReferenceType = InventoryReferenceType.Order, ReferenceId = "1" };
        var layer = new InventoryCostLayer { Id = 2, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            InventoryTransactionId = 2, UnitCost = unit };
        s.Entries.Add(new InventoryValuationEntry { Id = 2, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            InventoryTransactionId = 2, InventoryTransaction = tx, InventoryCostLayerId = 2, InventoryCostLayer = layer,
            EntryType = InventoryValuationEntryType.Revaluation, ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "1", ReferenceLineId = 1, RevaluationOfEntryId = 1,
            SourceValuationEntryId = dedicated ? null : 1, ReferenceSubKey = dedicated ? "REVAL:L2:A1" : null,
            CostSourceType = dedicated ? InventoryCostSourceType.RevaluationAdjustment : InventoryCostSourceType.Manual,
            Amount = (unit - 10) * 10 * (dedicated ? -1 : 1), OccurredAtUtc = At.AddMinutes(1) });
        s.ActivityEntryIds.Add(2);
    }
    internal static InventoryValuationEntry Mirror(InventoryValuationEntry root, decimal qty, decimal unit) => new() {
        Id = 100, StoreId = 1, WarehouseId = 1, ProductVariantId = 1, InventoryTransactionId = 100,
        InventoryTransaction = new InventoryTransaction { Id = 100, StoreId = 1, WarehouseId = 1, ProductVariantId = 1,
            TransactionType = InventoryTransactionType.CustomerReturnIn },
        EntryType = InventoryValuationEntryType.Inbound, SourceValuationEntryId = root.Id,
        Quantity = qty, UnitCost = unit, Amount = qty * unit, OccurredAtUtc = At.AddHours(1) };
    internal static void Return(ProfitSourceSnapshot s, decimal qty, decimal unit = 10, bool restock = true)
    {
        var id = s.Returns.Count + 1;
        s.Returns.Add(new SalesReturn { Id = id, StoreId = 1, OrderId = 1, POSShiftId = 1,
            Status = SalesReturnStatus.Completed, CompletedAtUtc = At.AddHours(1), ReturnSubtotal = qty * 20 });
        s.ReturnLines.Add(new SalesReturnLine { Id = id, StoreId = 1, SalesReturnId = id, OrderLineId = 1,
            VariantId = 1, ReturnBaseQuantity = qty,
            Action = restock ? SalesReturnLineAction.Restock : SalesReturnLineAction.NoRestock });
        if (!restock) return;
        var inbound = Mirror(s.Entries[0], qty, unit);
        inbound.Id += id; inbound.ReferenceType = InventoryReferenceType.Refund;
        inbound.ReferenceId = id.ToString(); inbound.ReferenceLineId = id;
        s.Entries.Add(inbound);
    }
}
