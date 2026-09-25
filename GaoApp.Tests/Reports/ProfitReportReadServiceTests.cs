using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Services.Reports;
using static GaoApp.Tests.Reports.ProfitReportAggregationPolicyTests;

namespace GaoApp.Tests.Reports;

public sealed class ProfitReportReadServiceTests
{
    [Fact]
    public async Task Current_and_compare_share_one_repository_snapshot_and_canonical_period()
    {
        var repo = new Reader(Snapshot()); var service = Service(repo);
        var result = await service.ReadAsync(Request("previous"));
        Assert.Equal(1, repo.Calls);
        Assert.NotNull(repo.Periods!.Comparison);
        Assert.Equal(repo.Periods.Current.Period.FromUtc, repo.Periods.Comparison!.Period.ToUtcExclusive);
        Assert.Equal(100, result.Current.GrossProfit.Value);
        Assert.Equal(0, result.Comparison!.NetSales.Value);
        Assert.Equal("previous", result.Query.Compare);
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("customer")]
    [InlineData("compare")]
    [InlineData("page")]
    [InlineData("sort")]
    public async Task Invalid_context_is_rejected_before_query_and_not_silently_normalized(string invalid)
    {
        var repo = new Reader(Snapshot()); var request = Request();
        if (invalid == "terminal") request.TerminalId = -1;
        if (invalid == "customer") request.CustomerState = "anything";
        if (invalid == "compare") request.Compare = "anything";
        if (invalid == "page") request.Page = 0;
        if (invalid == "sort") request.Sort = "margin";
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service(repo).ReadAsync(request));
        Assert.Equal(0, repo.Calls);
    }

    [Fact]
    public async Task Page_or_search_does_not_turn_whole_scope_cost_into_subset_total()
    {
        var s = Snapshot(); s.Lines.Clear();
        var query = Request(); query.Search = "missing"; query.PageSize = 1;
        var result = await Service(new Reader(s)).ReadAsync(query);
        Assert.Empty(result.Details); Assert.Equal(0, result.TotalItems);
        Assert.Equal(200, result.Current.NetSales.Value);
        Assert.Null(result.Current.Cogs.Value);
    }

    [Fact]
    public async Task Provisional_detail_exposes_source_line_without_inventing_line_profit()
    {
        var s = Snapshot(); var root = s.Entries[0]; root.IsProvisional = true;
        var allocation = root.CostLayerAllocations.Single();
        allocation.IsProvisional = true; allocation.IsResolved = false;
        allocation.ResolvedQuantity = 0; allocation.ResolvedAmount = 0;
        var query = Request(); query.Segment = "provisional";
        var result = await Service(new Reader(s)).ReadAsync(query);
        var row = Assert.Single(result.Details);
        Assert.Equal(1, row.OrderLineId); Assert.Equal("Rice", row.ItemName);
        Assert.Equal(100, row.Summary.ProvisionalCogs.Value); Assert.Null(row.Summary.GrossProfit.Value);
        Assert.Equal("Tạm tính", result.Current.CostState);
    }

    [Fact]
    public async Task Cancellation_and_store_change_cannot_emit_partial_response()
    {
        var repo = new Reader(Snapshot());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(repo).ReadAsync(Request(), false, cts.Token));
        var other = new ProfitSourceSnapshot { StoreId = 2 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new Reader(other)).ReadAsync(Request()));
    }

    [Fact]
    public async Task Activity_pagination_preserves_full_scope_summary_and_daily_chart()
    {
        var s = Snapshot(); ProfitReportAggregationPolicyTests.Finalize(s, 12);
        var query = Request(); query.Search = "not matched"; query.PageSize = 1;
        var result = await Service(new Reader(s)).ReadAsync(query, true);
        Assert.Empty(result.Activity!.Rows);
        Assert.Equal(20, result.Activity.Net); Assert.Single(result.Activity.Days);
        Assert.Equal("none", result.Query.Compare);
    }

    [Theory]
    [InlineData("newest", 2)]
    [InlineData("oldest", 1)]
    public async Task Same_timestamp_returns_have_stable_identity_order_across_pages(string sort, int firstId)
    {
        var s = Snapshot(); Return(s, 1, restock: false); Return(s, 1, restock: false);
        s.Returns.Reverse();
        var request = Request(); request.Sort = sort; request.PageSize = 1;
        // Only the return bucket: both events have the same OrderId, timestamp and kind.
        var policy = new SalesReportingPeriodPolicy();
        var period = policy.Resolve(new SalesExecutiveDashboardQueryDto {
            FromDate = At.Date, ToDate = At.Date, Compare = "none" }, At).Current.Period;
        request.Bucket = (int)((At.AddHours(1) - period.FromUtc).TotalHours / period.BucketHours);
        var service = Service(new Reader(s));
        var first = await service.ReadAsync(request);
        Assert.Equal(firstId, Assert.Single(first.Details).SalesReturnId);
        request.Page = 2;
        var second = await service.ReadAsync(request);
        Assert.Equal(3 - firstId, Assert.Single(second.Details).SalesReturnId);
        Assert.Equal(160, second.Current.NetSales.Value);
    }

    internal static ProfitReportQueryDto Request(string compare = "none") => new() {
        FromDate = At.Date, ToDate = At.Date, Compare = compare };
    private static ProfitReportReadService Service(Reader reader) =>
        new(reader, new Store(), new SalesReportingPeriodPolicy(), Policy());
    private sealed class Store : ICurrentStore { public int StoreId => 1; }
    private sealed class Reader(ProfitSourceSnapshot snapshot) : IProfitReportReadRepository
    {
        public int Calls; public SalesResolvedPeriodSet? Periods;
        public Task<ProfitSourceSnapshot> ReadAsync(int storeId, SalesResolvedPeriodSet periods,
            bool activity, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Calls++; Periods = periods; return Task.FromResult(snapshot); }
    }
}
