using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Reports;

public sealed class ManagementReportService(IProfitReportReadRepository repository, ICurrentStore store,
    SalesReportingPeriodPolicy periods, ProfitReportAggregationPolicy aggregation, ProfitReportExecutionGate gate) : IManagementReportService
{
    public Task<ManagementReportDto> ReadAsync(ManagementReportQueryDto query, CancellationToken ct = default)
        => gate.ExecuteAsync(token => ReadCoreAsync(query, token), ct);

    private async Task<ManagementReportDto> ReadCoreAsync(ManagementReportQueryDto query, CancellationToken ct)
    {
        if (store.StoreId <= 0) throw new InvalidOperationException("Cửa hàng hiện tại chưa hợp lệ.");
        if (query.TerminalId is <= 0 || query.Compare is not (null or "none" or "previous") ||
            query.CustomerState is not (null or "all" or "linked" or "guest"))
            throw new ArgumentException("Bộ lọc báo cáo không hợp lệ.");
        var resolved = periods.Resolve(new SalesExecutiveDashboardQueryDto { FromDate = query.FromDate,
            ToDate = query.ToDate, Compare = query.Compare, TerminalId = query.TerminalId,
            CustomerState = query.CustomerState }, DateTime.UtcNow);
        resolved.IncludeManagement = true;
        var source = await repository.ReadAsync(store.StoreId, resolved, false, ct);
        if (source.StoreId != store.StoreId) throw new InvalidOperationException("Cửa hàng báo cáo đã thay đổi.");
        var lineCosts = new Dictionary<int, ProfitReportAggregationPolicy.CostResult>();
        var costs = aggregation.EvaluateCosts(source, lineCosts, ct);
        ManagementPeriodDto Build(SalesResolvedExecutiveQueryDto q) => BuildPeriod(source, q,
            aggregation.Aggregate(source, q, costs, ct), lineCosts, ct);
        return new ManagementReportDto { GeneratedAtUtc = source.ReadAtUtc, Period = resolved.Current.Period,
            ComparisonPeriod = resolved.Comparison?.Period, Current = Build(resolved.Current),
            Comparison = resolved.Comparison is null ? null : Build(resolved.Comparison), Terminals = source.Terminals };
    }

    private sealed record LineEvent(int VariantId, string Name, int CategoryId, string CategoryName, string? Sku,
        int OrderId, string Tier, int CustomerId, string CustomerName, bool Sale, decimal Quantity, ProfitSummaryDto Summary);

    public ManagementPeriodDto BuildPeriod(ProfitSourceSnapshot source, SalesResolvedExecutiveQueryDto query,
        ProfitReportAggregationPolicy.PeriodResult result,
        Dictionary<int, ProfitReportAggregationPolicy.CostResult> lineCosts, CancellationToken ct = default)
    {
        var p = query.Period;
        var orders = source.Orders.ToDictionary(x => x.Id);
        var customers = source.Customers.ToDictionary(x => x.Id);
        var products = source.Products.ToDictionary(x => x.VariantId);
        var lines = source.Lines.Where(x => !x.IsDeleted).ToLookup(x => x.OrderId);
        var returns = source.ReturnLines.Where(x => !x.IsDeleted).ToLookup(x => x.SalesReturnId);
        string Tier(Order o) => o.CustomerPriceTierSnapshot is "WHOLESALE" or "RETAIL" ? o.CustomerPriceTierSnapshot :
            o.CustomerId is null ? "RETAIL" : customers.GetValueOrDefault(o.CustomerId.Value)?.PriceTier is "WHOLESALE" ? "WHOLESALE" :
            customers.ContainsKey(o.CustomerId.Value) ? "RETAIL" : "UNKNOWN";
        string CustomerName(Order o) => o.CustomerId is null ? "Khách vãng lai" : customers.GetValueOrDefault(o.CustomerId.Value)?.Name ?? "Khách hàng không còn trong danh mục";
        static ProfitReportAggregationPolicy.CostResult Cost(ProfitSummaryDto s) => new(s.Cogs.Value,
            s.ProvisionalCogs.Value, s.Cogs.Quality, s.AffectedLines);
        ProfitSummaryDto Combine(IEnumerable<ProfitSummaryDto> items) {
            var all = items.ToList();
            return ProfitReportAggregationPolicy.Summarize(result.Summary.NetSales.Available && all.All(x => x.NetSales.Available) ? all.Sum(x => x.NetSales.Value!.Value) : null,
                all.Select(Cost).ToList(), all.Count > 0);
        }
        var events = new List<LineEvent>();
        foreach (var row in result.Details)
        {
            ct.ThrowIfCancellationRequested();
            if (row.EventKind == "Void") continue;
            var order = orders[row.OrderId];
            var saleLines = lines[row.OrderId].OrderBy(x => x.Id).ToList();
            var returnLines = row.SalesReturnId is int id ? returns[id].OrderBy(x => x.Id).ToList() : [];
            var parts = row.EventKind == "Trả hàng"
                ? returnLines.Select(x => (x.VariantId, x.ItemName, Weight: Math.Max(0, x.RefundLineTotal), Qty: -x.ReturnBaseQuantity, LineId: x.OrderLineId)).ToList()
                : saleLines.Select(x => (VariantId: x.VariantId, x.ItemName, Weight: Math.Max(0, x.LineTotal), Qty: x.BaseQuantity, LineId: x.Id)).ToList();
            // Retain unmatched revenue explicitly so dimension totals still reconcile with the report.
            if (parts.Count == 0) parts.Add((0, "Không xác định sản phẩm", 1, 0, 0));
            var weights = parts.Sum(x => x.Weight);
            decimal? allocated = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                var line = parts[i];
                var revenue = i == parts.Count - 1 ? row.Summary.NetSales.Value - allocated :
                    row.Summary.NetSales.Value is decimal total ? Math.Round(total * (weights == 0 ? 1m / parts.Count : line.Weight / weights), 2) : (decimal?)null;
                allocated += revenue;
                var cost = row.EventKind == "Trả hàng" ? Cost(row.Summary) :
                    !row.Summary.Cogs.Available ? Cost(row.Summary) : lineCosts.GetValueOrDefault(line.LineId) ??
                        new ProfitReportAggregationPolicy.CostResult(null, null, ProfitQuality.Unavailable, null);
                var product = products.GetValueOrDefault(line.VariantId);
                events.Add(new(line.VariantId, line.ItemName, product?.CategoryId ?? 0, product?.CategoryName ?? "Chưa phân nhóm",
                    product?.Sku, row.OrderId, Tier(order), order.CustomerId ?? 0, CustomerName(order), row.EventKind == "Bán hàng",
                    line.Qty, ProfitReportAggregationPolicy.Summarize(revenue, [cost], true)));
            }
        }
        List<ReportDimensionDto> Dimensions<TKey>(Func<LineEvent, TKey> key, Func<IGrouping<TKey, LineEvent>, (int Id, string Name, string? Group, string? Sku)> describe)
            where TKey : notnull => events.GroupBy(key).Select(g => {
                var d = describe(g); return new ReportDimensionDto(d.Id, d.Name, d.Group, d.Sku,
                    g.Sum(x => x.Quantity), g.Where(x => x.Sale).Select(x => x.OrderId).Distinct().Count(), Combine(g.Select(x => x.Summary)));
            }).OrderByDescending(x => x.Summary.NetSales.Value).ThenBy(x => x.Name).ThenBy(x => x.Id).ToList();
        var expenses = source.OperatingExpenses.Where(x => x.StoreId == source.StoreId && !x.IsDeleted &&
            x.RecognitionFrom.Date <= p.ToDate.Date && x.RecognitionTo.Date >= p.FromDate.Date).ToList();
        var confirmed = expenses.Where(x => x.Status == "confirmed").ToList();
        var amount = confirmed.Sum(x => OperatingExpensePolicy.Allocate(x, p.FromDate, p.ToDate));
        var expenseTrend = Enumerable.Range(0, p.BucketCount).Select(i => {
            var start = periods.GetBucketLocalStart(p, i); var end = start.AddHours(p.BucketHours).AddTicks(-1);
            // Single-day hour buckets share the store expense uniformly, with exact-cent cumulative rounding.
            if (p.Granularity == SalesTrendGranularities.Hour) {
                var dayAmount = confirmed.Sum(x => OperatingExpensePolicy.Allocate(x, start.Date, start.Date));
                return Math.Round(dayAmount * (i + 1) / p.BucketCount, 2) - Math.Round(dayAmount * i / p.BucketCount, 2);
            }
            return confirmed.Sum(x => OperatingExpensePolicy.Allocate(x, start.Date,
                end.Date > p.ToDate.Date ? p.ToDate : end.Date));
        }).ToList();
        var buckets = result.Details.GroupBy(x => (int)((x.EventDate - p.FromUtc).TotalHours / p.BucketHours)).ToDictionary(x => x.Key, x => x.ToList());
        var groups = Dimensions(x => x.Tier, g => (g.Key == "WHOLESALE" ? 1 : g.Key == "RETAIL" ? 2 : 0,
            g.Key == "WHOLESALE" ? "Khách sỉ" : g.Key == "RETAIL" ? "Khách lẻ" : "Chưa xác định loại khách", g.Key, null));
        return new ManagementPeriodDto {
            Summary = result.Summary, OperatingExpenses = amount,
            OperatingProfit = query.TerminalId is null && query.CustomerState == "all" ? result.Summary.GrossProfit.Value - amount : null,
            DraftExpenses = expenses.Count(x => x.Status == "draft"), Trend = result.Trend, ExpenseTrend = expenseTrend,
            SalesOrders = result.Details.Where(x => x.EventKind == "Bán hàng").Select(x => x.OrderId).Distinct().Count(),
            InferredCustomerOrders = result.Details.Select(x => orders[x.OrderId]).Where(x => x.CustomerPriceTierSnapshot is null && x.CustomerId.HasValue).DistinctBy(x => x.Id).Count(),
            Products = Dimensions(x => x.VariantId, g => (g.Key, g.First().Name, g.First().CategoryName, g.First().Sku)),
            Categories = Dimensions(x => x.CategoryId, g => (g.Key, g.First().CategoryName, null, null)),
            Customers = Dimensions(x => (x.CustomerId, x.Tier), g => (g.Key.CustomerId, g.First().CustomerName, g.Key.Tier, null)),
            CustomerGroups = groups,
            ExpenseCategories = confirmed.GroupBy(x => x.Category).Select(g => new ExpenseCategoryDto(g.Key,
                OperatingExpensePolicy.Categories.GetValueOrDefault(g.Key) ?? g.Key,
                g.Sum(x => OperatingExpensePolicy.Allocate(x, p.FromDate, p.ToDate)))).OrderByDescending(x => x.Amount).ToList(),
            GroupTrend = Enumerable.Range(0, p.BucketCount).Select(i => {
                var rows = buckets.GetValueOrDefault(i) ?? [];
                decimal? Group(string tier) { var selected = rows.Where(x => Tier(orders[x.OrderId]) == tier).ToList();
                    return result.Summary.NetSales.Available ? selected.Sum(x => x.Summary.NetSales.Value) : null; }
                return new ReportGroupTrendDto(i, periods.FormatBucketLabel(p, i), Group("WHOLESALE"), Group("RETAIL"), Group("UNKNOWN"));
            }).ToList()
        };
    }
}
