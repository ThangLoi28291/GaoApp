using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Interfaces.Services.Reports;

namespace GaoApp.Application.Services.Reports;

public sealed class ProfitReportReadService(IProfitReportReadRepository repository, ICurrentStore store,
    SalesReportingPeriodPolicy periods, ProfitReportAggregationPolicy aggregation, ProfitReportExecutionGate? executionGate = null) : IProfitReportReadService
{
    public Task<ProfitReportResponseDto> ReadAsync(ProfitReportQueryDto query, bool activity = false,
        CancellationToken ct = default)
        => (executionGate ?? ProfitReportExecutionGate.Default).ExecuteAsync(token => ReadCoreAsync(query, activity, token), ct);

    private async Task<ProfitReportResponseDto> ReadCoreAsync(ProfitReportQueryDto query, bool activity = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (store.StoreId <= 0) throw new InvalidOperationException("Store hiện tại chưa hợp lệ.");
        if (query.TerminalId is <= 0 || query.CustomerState is not (null or "all" or "linked" or "guest") ||
            query.Compare is not (null or "none" or "previous"))
            throw new ArgumentException("Bộ lọc báo cáo không hợp lệ.");
        if (query.Segment is not (null or "profit" or "cogs" or "provisional") ||
            query.Sort is not (null or "newest" or "oldest") || query.Page < 1 ||
            query.PageSize is < 1 or > 100 || query.Search?.Length > 100)
            throw new ArgumentException("Bộ lọc chi tiết không hợp lệ.");
        var resolved = periods.Resolve(new SalesExecutiveDashboardQueryDto {
            FromDate = query.FromDate, ToDate = query.ToDate, Compare = activity ? "none" : query.Compare,
            TerminalId = query.TerminalId, CustomerState = query.CustomerState }, DateTime.UtcNow);
        if (query.Bucket is int bucket && (bucket < 0 || bucket >= resolved.Current.Period.BucketCount))
            throw new ArgumentException("Khoảng thời gian chi tiết không hợp lệ.");
        var snapshot = await repository.ReadAsync(store.StoreId, resolved, activity, ct);
        ct.ThrowIfCancellationRequested();
        if (snapshot.StoreId != store.StoreId) throw new InvalidOperationException("Reporting Store changed.");
        var lineCosts = new Dictionary<int, ProfitReportAggregationPolicy.CostResult>();
        var costs = aggregation.EvaluateCosts(snapshot, lineCosts, ct);
        var current = aggregation.Aggregate(snapshot, resolved.Current, costs, ct);
        var comparison = resolved.Comparison is null ? null : aggregation.Aggregate(snapshot, resolved.Comparison, costs, ct);
        var rows = current.Details.AsEnumerable();
        if (query.Segment == "provisional")
        {
            var origins = current.Details.Where(x => x.EventKind != "Trả hàng").ToDictionary(x => x.OrderId);
            rows = snapshot.Lines.Where(x => origins.ContainsKey(x.OrderId) &&
                    lineCosts.TryGetValue(x.Id, out var c) && c.Quality == ProfitQuality.Provisional)
                .Select(x => new ProfitDetailRowDto(x.OrderId, origins[x.OrderId].OrderNumber,
                    origins[x.OrderId].SaleDate, origins[x.OrderId].EventDate, "Dòng tạm tính",
                    ProfitReportAggregationPolicy.Summarize(null, [lineCosts[x.Id]], true), x.Id, x.ItemName));
        }
        if (query.Bucket is int index)
        {
            var start = resolved.Current.Period.FromUtc.AddHours((long)index * resolved.Current.Period.BucketHours);
            var end = start.AddHours(resolved.Current.Period.BucketHours);
            rows = rows.Where(x => x.EventDate >= start && x.EventDate < end);
        }
        if (query.Segment == "provisional") rows = rows.Where(x => x.Summary.Cogs.Quality == ProfitQuality.Provisional);
        if (!string.IsNullOrWhiteSpace(query.Search))
            rows = rows.Where(x => x.OrderNumber.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase));
        rows = query.Sort == "oldest" ? rows.OrderBy(x => x.EventDate).ThenBy(x => x.OrderId).ThenBy(x => x.EventKind)
                .ThenBy(x => x.SalesReturnId).ThenBy(x => x.OrderLineId)
            : rows.OrderByDescending(x => x.EventDate).ThenByDescending(x => x.OrderId).ThenBy(x => x.EventKind)
                .ThenByDescending(x => x.SalesReturnId).ThenByDescending(x => x.OrderLineId);
        var filtered = rows.ToList();
        var response = new ProfitReportResponseDto {
            GeneratedAtUtc = snapshot.ReadAtUtc,
            Query = new ProfitReportQueryDto {
                FromDate = resolved.Current.Period.FromDate, ToDate = resolved.Current.Period.ToDate,
                Compare = resolved.ComparisonMode, TerminalId = resolved.Current.TerminalId,
                CustomerState = resolved.Current.CustomerState, Page = query.Page, PageSize = query.PageSize,
                Segment = query.Segment ?? "profit", Search = query.Search, Sort = query.Sort ?? "newest", Bucket = query.Bucket },
            Period = resolved.Current.Period, ComparisonPeriod = resolved.Comparison?.Period,
            Current = current.Summary, Comparison = comparison?.Summary,
            Trend = current.Trend, ComparisonTrend = comparison?.Trend ?? [],
            Details = filtered.Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue))
                .Take(query.PageSize).ToList(),
            TotalItems = filtered.Count, Terminals = snapshot.Terminals
        };
        if (activity)
        {
            response.Activity = aggregation.Activity(snapshot, resolved.Current, costs, ct);
            var activityRows = response.Activity.Rows.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(query.Search))
                activityRows = activityRows.Where(x => x.OrderNumber.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase));
            activityRows = query.Sort == "oldest" ? activityRows.OrderBy(x => x.AdjustmentDate).ThenBy(x => x.EntryId)
                : activityRows.OrderByDescending(x => x.AdjustmentDate).ThenByDescending(x => x.EntryId);
            var all = activityRows.ToList();
            response.TotalItems = all.Count;
            response.Activity.Rows = all.Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue))
                .Take(query.PageSize).ToList();
        }
        return response;
    }
}
