using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Services.Reports;
using static GaoApp.Tests.Reports.ProfitReportAggregationPolicyTests;

namespace GaoApp.Tests.Reports;

public sealed class ReportCostDiagnosticsTests
{
    [Theory]
    [InlineData("missing", "MissingOutboundValuation", ProfitQuality.Unavailable)]
    [InlineData("quantity", "OutboundQuantityMismatch", ProfitQuality.Unavailable)]
    [InlineData("transaction", "Valuation:Original sale transaction is missing.", ProfitQuality.Unavailable)]
    [InlineData("variant", "ValuationLineMismatch", ProfitQuality.DataIntegrityConflict)]
    [InlineData("orphan", "OrphanValuation", ProfitQuality.DataIntegrityConflict)]
    public void Reports_name_actual_missing_or_conflicting_source_without_inventing_cost(string mutation, string code, ProfitQuality quality)
    {
        var source = Snapshot(); source.Orders[0].OrderNumber = "POS-TEST"; source.Lines[0].ItemName = "Aquafina";
        switch (mutation) {
            case "missing": source.Entries.Clear(); break;
            case "quantity": source.Lines[0].BaseQuantity = 11; break;
            case "transaction": source.Entries[0].InventoryTransaction = null!; break;
            case "variant": source.Entries[0].ProductVariantId = 999; break;
            case "orphan": source.Entries[0].ReferenceLineId = 999; break;
        }
        var issues = new List<ProfitCostIssueDto>(); var policy = Policy();
        var costs = policy.EvaluateCosts(source, issues: issues);
        var period = policy.Aggregate(source, Query(), costs);
        var diagnostic = ProfitCostDiagnosticsPolicy.ForPeriod(issues, period.Details);
        Assert.Equal(200, period.Summary.NetSales.Value); Assert.Null(period.Summary.Cogs.Value);
        Assert.Equal(quality, period.Summary.Cogs.Quality);
        Assert.Equal(1, diagnostic.AffectedOrders);
        var issue = Assert.Single(diagnostic.Issues, x => x.Code == code);
        Assert.Equal("POS-TEST", issue.OrderNumber); Assert.Equal(quality, issue.Quality);
        Assert.NotEmpty(issue.Message);
        if (mutation != "orphan") { Assert.Equal("Aquafina", issue.ItemName); Assert.Equal(1, diagnostic.AffectedLines); }
    }

    [Fact]
    public void Diagnostics_count_distinct_orders_and_lines_and_never_leak_other_periods_or_filters()
    {
        var issues = Enumerable.Range(1, 40).Select(i => new ProfitCostIssueDto(i, "POS-" + i, i, "Product",
            ProfitQuality.Unavailable, "Missing", "Missing source")).ToList();
        issues.Add(issues[0]);
        var rows = Enumerable.Range(1, 30).Select(i => new ProfitDetailRowDto(i, "POS-" + i, At, At, "Bán hàng", new())).ToList();
        rows.Add(rows[0] with { EventKind = "Trả hàng" });
        var diagnostic = ProfitCostDiagnosticsPolicy.ForPeriod(issues, rows);
        Assert.Equal(30, diagnostic.AffectedOrders); Assert.Equal(30, diagnostic.AffectedLines);
        Assert.Equal(30, diagnostic.UnavailableOrders); Assert.Equal(30, diagnostic.TotalIssues);
        Assert.Equal(20, diagnostic.Issues.Count); Assert.DoesNotContain(diagnostic.Issues, x => x.OrderId > 30);
        Assert.Empty(ProfitCostDiagnosticsPolicy.ForPeriod(issues, []).Issues);
    }
}
