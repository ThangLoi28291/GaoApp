using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Reports;

public sealed class ProfitReportAggregationPolicy(SalesReportingPeriodPolicy periods)
{
    public sealed record CostResult(decimal? Cost, decimal? Exposure, ProfitQuality Quality, int? AffectedLines);
    public sealed record PeriodResult(ProfitSummaryDto Summary, List<ProfitTrendPointDto> Trend,
        List<ProfitDetailRowDto> Details);

    public static ProfitSummaryDto Summarize(decimal? revenue, IReadOnlyCollection<CostResult> costs, bool hasEvents)
    {
        var quality = costs.Any(x => x.Quality == ProfitQuality.DataIntegrityConflict)
            ? ProfitQuality.DataIntegrityConflict
            : costs.Any(x => !x.Cost.HasValue || !x.Exposure.HasValue)
                ? ProfitQuality.Unavailable
                : costs.Any(x => x.Quality == ProfitQuality.Provisional) ? ProfitQuality.Provisional
                : hasEvents ? ProfitQuality.Finalized : ProfitQuality.Empty;
        var valid = quality is ProfitQuality.Finalized or ProfitQuality.Provisional or ProfitQuality.Empty;
        decimal? cost = valid ? costs.Sum(x => x.Cost!.Value) : null;
        decimal? exposure = valid ? costs.Sum(x => x.Exposure!.Value) : null;
        var profit = revenue - cost;
        return new ProfitSummaryDto
        {
            NetSales = new(revenue, revenue.HasValue ? ProfitQuality.Finalized : ProfitQuality.Unavailable),
            Cogs = new(cost, quality), GrossProfit = new(profit, quality),
            GrossMargin = new(revenue is not null and not 0 ? profit / revenue * 100m : null, quality),
            ProvisionalCogs = new(exposure, quality),
            CostState = quality switch {
                ProfitQuality.Finalized => "Đã xác định", ProfitQuality.Provisional => "Tạm tính",
                ProfitQuality.Empty => "—", _ => "Chưa đủ dữ liệu" },
            AffectedOrders = valid ? costs.Count(x => x.Quality == ProfitQuality.Provisional) : null,
            AffectedLines = valid ? costs.Sum(x => x.AffectedLines ?? 0) : null,
            IsEmpty = !hasEvents && valid,
            Message = !valid ? "Chưa đủ dữ liệu tin cậy để xác định giá vốn cho phạm vi này."
                : quality == ProfitQuality.Provisional ? "Giá vốn và lợi nhuận còn tạm tính." : null
        };
    }

    public Dictionary<int, CostResult> EvaluateCosts(ProfitSourceSnapshot source, Dictionary<int, CostResult>? lineResults = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var index = new EvidenceIndex(source);
        var entriesByLine = source.Entries.Where(x => x.ReferenceType == InventoryReferenceType.Order)
            .ToLookup(x => (x.ReferenceId, x.ReferenceLineId));
        var children = source.Entries.SelectMany(x => new[] { x.RevaluationOfEntryId, x.SourceValuationEntryId }
            .OfType<int>().Distinct().Select(id => (id, entry: x))).ToLookup(x => x.id, x => x.entry);
        var lines = source.Lines.ToLookup(x => x.OrderId);
        var allocations = source.LegalAllocations.ToLookup(x => x.OrderLineId);
        var returnLines = source.ReturnLines.ToLookup(x => x.OrderLineId);
        var returnHeaders = source.Returns.ToDictionary(x => x.Id);
        var result = new Dictionary<int, CostResult>();
        foreach (var order in source.Orders)
        {
            ct.ThrowIfCancellationRequested();
            var orderLines = lines[order.Id].Where(x => !x.IsDeleted).ToList();
            var outcomes = new List<CostResult>();
            if (order.IsDeleted || order.StoreId != source.StoreId || orderLines.Count == 0)
            { result[order.Id] = Unknown(); continue; }
            var orderLineIds = orderLines.Select(x => x.Id).ToHashSet();
            foreach (var line in orderLines)
            {
                ct.ThrowIfCancellationRequested();
                var entries = entriesByLine[(order.Id.ToString(), (int?)line.Id)].ToList();
                var roots = entries.Where(x => x.EntryType == InventoryValuationEntryType.Outbound).ToList();
                var quantity = line.BaseQuantity > 0 ? line.BaseQuantity : line.Quantity * (line.Multiplier > 0 ? line.Multiplier : 1);
                if (line.StoreId != source.StoreId || roots.Count == 0 || roots.Sum(x => -x.Quantity) != quantity)
                { outcomes.Add(Unknown()); continue; }
                if (entries.Any(x => x.EntryType == InventoryValuationEntryType.Revaluation &&
                    !roots.Any(r => r.Id == x.RevaluationOfEntryId)) ||
                    roots.Any(x => x.ProductVariantId != line.VariantId))
                { outcomes.Add(Conflict()); continue; }
                var lineMaps = allocations[line.Id].ToList();
                if (lineMaps.Count > 0 && (lineMaps.Sum(x => x.BaseQuantity) != quantity ||
                    lineMaps.Any(x => x.InventoryTransactionId is null ||
                        roots.Where(r => r.InventoryTransactionId == x.InventoryTransactionId).Sum(r => -r.Quantity) != x.BaseQuantity)))
                { outcomes.Add(Conflict()); continue; }
                if (order.UseMultiLegalEntity && lineMaps.Count == 0)
                { outcomes.Add(Unknown()); continue; }
                var lineReturns = returnLines[line.Id].Where(x => !x.IsDeleted &&
                    returnHeaders.TryGetValue(x.SalesReturnId, out var header) && !header.IsDeleted &&
                    header.Status == SalesReturnStatus.Completed).ToList();
                if (lineReturns.Sum(x => x.ReturnBaseQuantity) > quantity ||
                    lineReturns.Any(x => x.StoreId != source.StoreId || x.VariantId != line.VariantId ||
                        x.ReturnBaseQuantity <= 0 || returnHeaders[x.SalesReturnId].OrderId != order.Id))
                { outcomes.Add(Conflict()); continue; }
                var lineCost = new List<CostResult>();
                foreach (var root in roots)
                {
                    var linked = children[root.Id].DistinctBy(x => x.Id).ToList();
                    var cost = SaleValuationCostPolicy.Evaluate(root,
                        linked.Where(x => x.EntryType == InventoryValuationEntryType.Revaluation).ToList(),
                        linked.Where(x => x.SourceValuationEntryId == root.Id).ToList());
                    var quality = cost.State switch {
                        SaleValuationCostPolicy.Quality.Finalized => ProfitQuality.Finalized,
                        SaleValuationCostPolicy.Quality.Provisional or SaleValuationCostPolicy.Quality.PartiallyFinalized
                            => ProfitQuality.Provisional,
                        SaleValuationCostPolicy.Quality.DataIntegrityConflict => ProfitQuality.DataIntegrityConflict,
                        _ => ProfitQuality.Unavailable };
                    var current = new CostResult(cost.Cost, cost.ProvisionalExposure, quality,
                        quality == ProfitQuality.Provisional ? 1 : 0);
                    if (root.CostLayerAllocations.Any(x => x.IsDeleted) ||
                        linked.Any(x => x.EntryType == InventoryValuationEntryType.Inbound &&
                            !ValidReturnOrVoid(source, index, order, line, x, returnHeaders)))
                        current = Conflict();
                    if (order.Status == OrderStatus.Voided &&
                        (cost.State != SaleValuationCostPolicy.Quality.Finalized || cost.Cost != 0 ||
                         cost.InventoryReversedQuantity != -root.Quantity)) current = Conflict();
                    if (lineMaps.Count > 0 && !ValidLegalMapping(source, index, order, line, root, lineMaps, linked))
                        current = Conflict();
                    lineCost.Add(current);
                }
                // Every completed Restock must have complete monetary/quantity provenance, including broken links.
                foreach (var returned in lineReturns)
                {
                    var inbound = index.RefundEntries[(returned.SalesReturnId.ToString(), (int?)returned.Id)]
                        .Where(x => x.EntryType == InventoryValuationEntryType.Inbound).ToList();
                    if (returned.Action == SalesReturnLineAction.Restock &&
                        (inbound.Sum(x => x.Quantity) != returned.ReturnBaseQuantity ||
                         inbound.Any(x => !roots.Any(r => r.Id == x.SourceValuationEntryId))) ||
                        returned.Action == SalesReturnLineAction.NoRestock && inbound.Count > 0)
                        lineCost.Add(Conflict());
                }
                var subtotal = Summarize(0, lineCost, true);
                if (lineResults is not null) lineResults[line.Id] = new(subtotal.Cogs.Value,
                    subtotal.ProvisionalCogs.Value, subtotal.Cogs.Quality, subtotal.AffectedLines);
                outcomes.Add(new(subtotal.Cogs.Value, subtotal.ProvisionalCogs.Value, subtotal.Cogs.Quality,
                    subtotal.Cogs.Quality == ProfitQuality.Provisional ? 1 : 0));
            }
            // An entry naming a missing/deleted line is evidence, not an invisible zero.
            if (index.OrderEntries[order.Id.ToString()].Any(x =>
                !x.ReferenceLineId.HasValue || !orderLineIds.Contains(x.ReferenceLineId.Value)))
                outcomes.Add(Conflict());
            var summary = Summarize(0, outcomes, true);
            result[order.Id] = new(summary.Cogs.Value, summary.ProvisionalCogs.Value,
                summary.Cogs.Quality, summary.AffectedLines);
        }
        return result;
    }

    private static bool ValidReturnOrVoid(ProfitSourceSnapshot s, EvidenceIndex index, Order order, OrderLine line,
        InventoryValuationEntry entry, Dictionary<int, SalesReturn> headers)
    {
        if (entry.ReferenceType == InventoryReferenceType.Order)
            return order.Status == OrderStatus.Voided;
        if (!int.TryParse(entry.ReferenceId, out var id) || !headers.TryGetValue(id, out var header) ||
            header.IsDeleted || header.Status != SalesReturnStatus.Completed ||
            header.StoreId != s.StoreId || header.OrderId != order.Id) return false;
        if (!entry.ReferenceLineId.HasValue) return false;
        return index.ReturnLines[entry.ReferenceLineId.Value].Any(x => !x.IsDeleted &&
            x.SalesReturnId == id && x.OrderLineId == line.Id && x.StoreId == s.StoreId &&
            x.Action == SalesReturnLineAction.Restock && x.VariantId == line.VariantId);
    }

    private static bool ValidLegalMapping(ProfitSourceSnapshot s, EvidenceIndex index, Order order, OrderLine line,
        InventoryValuationEntry root, List<OrderLegalEntityAllocation> maps, List<InventoryValuationEntry> linked)
    {
        var matches = maps.Where(x => x.InventoryTransactionId == root.InventoryTransactionId).ToList();
        if (matches.Count != 1) return false;
        var map = matches[0];
        if (map.IsDeleted || map.StoreId != s.StoreId || map.OrderId != order.Id ||
            map.ProductVariantId != line.VariantId || map.WarehouseId != root.WarehouseId ||
            root.Warehouse?.LegalEntityId != map.LegalEntityId ||
            !index.LegalEntities.Contains((map.LegalEntityId, s.StoreId)))
            return false;
        var reversals = index.LegalReversals[root.Id].ToList();
        if (reversals.Any(x => x.IsDeleted || x.StoreId != s.StoreId ||
            x.OrderLegalEntityAllocationId != map.Id || x.OrderId != order.Id || x.OrderLineId != line.Id ||
            x.LegalEntityId != map.LegalEntityId || x.WarehouseId != map.WarehouseId ||
            x.ProductVariantId != map.ProductVariantId || x.BaseQuantity <= 0) ||
            reversals.Sum(x => x.BaseQuantity) > -root.Quantity) return false;
        foreach (var inbound in linked.Where(x => x.EntryType == InventoryValuationEntryType.Inbound))
            if (reversals.Where(x => x.InventoryTransactionId == inbound.InventoryTransactionId)
                .Sum(x => x.BaseQuantity) != inbound.Quantity) return false;
        foreach (var reversal in reversals)
        {
            if (reversal.ReversalType == OrderLegalEntityReversalType.Void)
            {
                if (order.Status != OrderStatus.Voided || reversal.SalesReturnId is not null) return false;
            }
            else
            {
                if (!reversal.SalesReturnLineId.HasValue) return false;
                var returned = index.ReturnLines[reversal.SalesReturnLineId.Value].FirstOrDefault(x => !x.IsDeleted &&
                    x.OrderLineId == line.Id && x.SalesReturnId == reversal.SalesReturnId && x.StoreId == s.StoreId);
                if (returned is null || !index.ReturnHeaders[returned.SalesReturnId].Any(x => !x.IsDeleted &&
                    x.Status == SalesReturnStatus.Completed && x.OrderId == order.Id && x.StoreId == s.StoreId)) return false;
                if (reversal.ReversalType == OrderLegalEntityReversalType.ReturnNoRestock)
                {
                    if (returned.Action != SalesReturnLineAction.NoRestock || reversal.InventoryTransactionId is not null) return false;
                    continue;
                }
                if (reversal.ReversalType != OrderLegalEntityReversalType.ReturnRestock || returned.Action != SalesReturnLineAction.Restock)
                    return false;
            }
            if (!linked.Any(x => x.EntryType == InventoryValuationEntryType.Inbound &&
                x.InventoryTransactionId == reversal.InventoryTransactionId)) return false;
        }
        return true;
    }

    public PeriodResult Aggregate(ProfitSourceSnapshot s, SalesResolvedExecutiveQueryDto query,
        Dictionary<int, CostResult> costs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var orders = s.Orders.Where(x => !x.IsDeleted).ToLookup(x => x.Id);
        var shifts = s.Shifts.ToLookup(x => x.Id);
        var rows = new List<ProfitDetailRowDto>();
        var period = query.Period;
        bool InPeriod(DateTime? date) => date >= period.FromUtc && date < period.ToUtcExclusive;
        var invalidRevenue = false;
        foreach (var order in s.Orders.Where(x => !x.IsDeleted && InPeriod(x.CompletedAtUtc) &&
            (x.Status == OrderStatus.Completed || x.Status == OrderStatus.Refunded || x.Status == OrderStatus.Voided)))
        {
            ct.ThrowIfCancellationRequested();
            var eligible = Matches(s, shifts, query, order, order.POSShiftId);
            if (eligible is null) { invalidRevenue = true; continue; }
            if (!eligible.Value) continue;
            var net = order.Status == OrderStatus.Voided ? 0 : order.Subtotal - order.DiscountTotal;
            var summary = Summarize(net, [costs.GetValueOrDefault(order.Id) ?? Unknown()], true);
            rows.Add(new(order.Id, order.OrderNumber ?? order.Id.ToString(), order.CompletedAtUtc, order.CompletedAtUtc!.Value,
                order.Status == OrderStatus.Voided ? "Void" : "Bán hàng", summary));
        }
        foreach (var returned in s.Returns.Where(x => !x.IsDeleted && x.Status == SalesReturnStatus.Completed &&
                     InPeriod(x.CompletedAtUtc)))
        {
            ct.ThrowIfCancellationRequested();
            var order = orders[returned.OrderId].FirstOrDefault();
            var eligible = order is null ? null : Matches(s, shifts, query, order, returned.POSShiftId);
            if (eligible is null) { invalidRevenue = true; continue; }
            if (!eligible.Value) continue;
            var diagnostic = costs.GetValueOrDefault(returned.OrderId) ?? Unknown();
            // Revenue event date remains here; cost belongs exclusively to its original sale period.
            var cost = diagnostic.Cost.HasValue ? new CostResult(0, 0, ProfitQuality.Finalized, 0) : diagnostic;
            rows.Add(new(returned.OrderId, order!.OrderNumber ?? order.Id.ToString(), order.CompletedAtUtc,
                returned.CompletedAtUtc!.Value, "Trả hàng", Summarize(-returned.ReturnSubtotal, [cost], true),
                SalesReturnId: returned.Id));
        }
        ProfitSummaryDto Combine(List<ProfitDetailRowDto> selected, bool unknown)
        {
            var selectedCosts = selected.Select(x => new CostResult(x.Summary.Cogs.Value,
                x.Summary.ProvisionalCogs.Value, x.Summary.Cogs.Quality, x.Summary.AffectedLines)).ToList();
            if (unknown) selectedCosts.Add(Unknown());
            return Summarize(unknown ? null : selected.Sum(x => x.Summary.NetSales.Value ?? 0),
                selectedCosts, selected.Count > 0 || unknown);
        }
        var bucketTicks = (long)period.BucketHours * TimeSpan.TicksPerHour;
        var rowsByBucket = rows.ToLookup(x => (int)((x.EventDate.Ticks - period.FromUtc.Ticks) / bucketTicks));
        var trend = Enumerable.Range(0, period.BucketCount).Select(i =>
        {
            ct.ThrowIfCancellationRequested();
            return new ProfitTrendPointDto(i, periods.FormatBucketLabel(period, i),
                Combine(rowsByBucket[i].ToList(), invalidRevenue));
        }).ToList();
        return new(Combine(rows, invalidRevenue), trend, rows);
    }

    public ProfitActivityDto Activity(ProfitSourceSnapshot s, SalesResolvedExecutiveQueryDto query,
        Dictionary<int, CostResult> costs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var entries = s.Entries.ToLookup(x => x.Id);
        var orders = s.Orders.ToLookup(x => x.Id.ToString());
        var shifts = s.Shifts.ToLookup(x => x.Id);
        var rows = new List<ProfitActivityRowDto>();
        foreach (var entry in s.Entries.Where(x => s.ActivityEntryIds.Contains(x.Id)))
        {
            ct.ThrowIfCancellationRequested();
            var root = entry.RevaluationOfEntryId is int rootId ? entries[rootId].FirstOrDefault() : null;
            var order = root is null ? null : orders[root.ReferenceId].FirstOrDefault();
            var matches = order is null ? null : Matches(s, shifts, query, order, order.POSShiftId);
            if (matches == false) continue;
            var known = matches == true && !entry.IsDeleted && order is not null &&
                costs.TryGetValue(order.Id, out var cost) && cost.Cost.HasValue;
            decimal? impact = known ? entry.CostSourceType switch {
                InventoryCostSourceType.Manual => entry.Amount,
                InventoryCostSourceType.RevaluationAdjustment => -entry.Amount, _ => null } : null;
            rows.Add(new(entry.Id, order?.Id, order?.OrderNumber ?? "Không xác định", order?.CompletedAtUtc,
                entry.OccurredAtUtc, impact, impact.HasValue ? ProfitQuality.Finalized : ProfitQuality.Unavailable));
        }
        var complete = rows.All(x => x.Impact.HasValue);
        return new ProfitActivityDto {
            Days = rows.GroupBy(x => periods.ConvertUtcToLocal(x.AdjustmentDate).Date).OrderBy(x => x.Key)
                .Select(g => new ProfitActivityDayDto(g.Key,
                    g.All(x => x.Impact.HasValue) ? g.Sum(x => Math.Max(0, x.Impact!.Value)) : null,
                    g.All(x => x.Impact.HasValue) ? g.Sum(x => Math.Max(0, -x.Impact!.Value)) : null,
                    g.All(x => x.Impact.HasValue) ? ProfitQuality.Finalized : ProfitQuality.Unavailable)).ToList(),
            Rows = rows, Increase = complete ? rows.Sum(x => Math.Max(0, x.Impact!.Value)) : null,
            Decrease = complete ? rows.Sum(x => Math.Max(0, -x.Impact!.Value)) : null,
            Net = complete ? rows.Sum(x => x.Impact!.Value) : null, Count = complete ? rows.Count : null,
            Quality = !complete ? ProfitQuality.Unavailable : rows.Count == 0 ? ProfitQuality.Empty : ProfitQuality.Finalized };
    }

    private static bool? Matches(ProfitSourceSnapshot s, ILookup<int, POSShift> shifts, SalesResolvedExecutiveQueryDto q, Order order, int shiftId)
    {
        if (order.StoreId != s.StoreId) return null;
        if (q.TerminalId.HasValue)
        {
            var shift = shifts[shiftId].FirstOrDefault(x => x.StoreId == s.StoreId && !x.IsDeleted);
            if (shift is null) return null;
            if (shift.TerminalId != q.TerminalId) return false;
        }
        return q.CustomerState switch { "linked" => order.CustomerId.HasValue, "guest" => !order.CustomerId.HasValue, _ => true };
    }

    private sealed class EvidenceIndex(ProfitSourceSnapshot source)
    {
        public ILookup<string, InventoryValuationEntry> OrderEntries { get; } = source.Entries
            .Where(x => x.ReferenceType == InventoryReferenceType.Order).ToLookup(x => x.ReferenceId);
        public ILookup<(string, int?), InventoryValuationEntry> RefundEntries { get; } = source.Entries
            .Where(x => x.ReferenceType == InventoryReferenceType.Refund).ToLookup(x => (x.ReferenceId, x.ReferenceLineId));
        public ILookup<int, SalesReturnLine> ReturnLines { get; } = source.ReturnLines.ToLookup(x => x.Id);
        public ILookup<int, SalesReturn> ReturnHeaders { get; } = source.Returns.ToLookup(x => x.Id);
        public ILookup<int, OrderLegalEntityAllocationReversal> LegalReversals { get; } = source.LegalReversals.ToLookup(x => x.SourceValuationEntryId);
        public HashSet<(int, int)> LegalEntities { get; } = source.LegalEntities.Where(x => !x.IsDeleted).Select(x => (x.Id, x.StoreId)).ToHashSet();
    }

    private static CostResult Unknown() => new(null, null, ProfitQuality.Unavailable, null);
    private static CostResult Conflict() => new(null, null, ProfitQuality.DataIntegrityConflict, null);
}
