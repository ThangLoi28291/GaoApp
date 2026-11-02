using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Reports;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Reports;

[Collection("DeliveryD08")]
public sealed class DeliveryProfitReportTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(4, false, null, ProfitQuality.Finalized, 36000)]
    [InlineData(2.5, false, null, ProfitQuality.Finalized, 22500)]
    [InlineData(3, true, null, ProfitQuality.Finalized, 26283)]
    [InlineData(3, false, 1, ProfitQuality.Provisional, 15000)]
    [InlineData(3, false, 0, ProfitQuality.Unavailable, null)]
    public async Task Real_delivery_handover_reports_exact_delivered_cost_without_second_stock_issue_or_writes(
        double delivered, bool opening, int? stock, ProfitQuality quality, int? expected)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture, stock: stock, opening: opening);
        var ack = await c.ConfirmAsync(await c.RequestAsync((decimal)delivered));
        var sale = await c.SaleAsync(ack); var before = await c.SnapshotAsync();
        var day = TimeZoneInfo.ConvertTimeFromUtc(sale.CompletedAtUtc!.Value,
            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")).Date;
        var url = $"/admin/reports/management/data?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}&compare=none&terminalId={c.Closer.Store.TerminalId}";
        var report = (await c.Client.Http.GetFromJsonAsync<ManagementReportDto>(url + "&view=overview"))!;
        Assert.Equal(sale.GrandTotal, report.Current.Summary.NetSales.Value);
        Assert.Equal(expected.HasValue ? (decimal?)expected.Value : null, report.Current.Summary.Cogs.Value);
        Assert.Equal(quality, report.Current.Summary.Cogs.Quality);
        Assert.Equal(sale.GrandTotal - expected, report.Current.Summary.GrossProfit.Value);
        Assert.Equal(report.Current.Summary.Cogs.Value, Assert.Single(report.Current.Products).Summary.Cogs.Value);
        Assert.Equal(1, report.Current.SalesOrders);
        var full = (await c.Client.Http.GetFromJsonAsync<ManagementReportDto>(url))!;
        Assert.Equal(JsonSerializer.Serialize(full.Current.Summary), JsonSerializer.Serialize(report.Current.Summary));
        Assert.Equal(JsonSerializer.Serialize(full.Current.Products.Take(6)), JsonSerializer.Serialize(report.Current.Products));
        var problemRows = (await c.Client.Http.GetFromJsonAsync<ProfitReportResponseDto>(
            $"/admin/reports/profit/data?fromDate={day:yyyy-MM-dd}&toDate={day:yyyy-MM-dd}&compare=none&terminalId={c.Closer.Store.TerminalId}&segment=issues"))!;
        Assert.Equal(expected.HasValue ? 0 : 1, problemRows.TotalItems);
        if (!expected.HasValue) Assert.Contains("chưa có đơn giá", Assert.Single(problemRows.Details).Summary.Message);
        if (expected.HasValue) Assert.Empty(report.Current.CostDiagnostics.Issues);
        else Assert.Contains(report.Current.CostDiagnostics.Issues, x => x.OrderId == sale.Id && x.Message.Contains("chưa có đơn giá"));
        Assert.Equal(before, await c.SnapshotAsync());
        await using var db = c.Context();
        Assert.Empty(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == sale.Id.ToString()).ToArrayAsync());

        // Damage detached evidence only; the real immutable ledger is never altered by these checks.
        var periods = new SalesReportingPeriodPolicy().Resolve(new SalesExecutiveDashboardQueryDto {
            FromDate = day, ToDate = day, Compare = "none", TerminalId = c.Closer.Store.TerminalId
        }, DateTime.UtcNow);
        var policy = new ProfitReportAggregationPolicy(new SalesReportingPeriodPolicy());
        foreach (var damage in new[] { "hash", "fragment", "allocation", "cross-store" })
        {
            var clone = await new ProfitReportReadRepository(db).ReadAsync(db.CurrentStoreId!.Value, periods, false);
            if (damage == "hash") clone.DeliveryRevisions.Single(x => x.DeliveryOrderId == c.Id).SnapshotHash = "BAD";
            if (damage == "fragment") clone.DeliveryFragments.First(x => x.DeliveryOrderId == c.Id).CostAmount += 1;
            if (damage == "allocation") clone.LegalAllocations.Single(x => x.OrderId == sale.Id).WarehouseId = 999;
            if (damage == "cross-store") clone.Deliveries.Single(x => x.Id == c.Id).StoreId += 1;
            var issues = new List<ProfitCostIssueDto>(); var costs = policy.EvaluateCosts(clone, issues: issues);
            Assert.Null(costs[sale.Id].Cost); Assert.Equal(ProfitQuality.DataIntegrityConflict, costs[sale.Id].Quality);
            Assert.Contains(issues, x => x.OrderId == sale.Id);
        }
        var adjusted = await new ProfitReportReadRepository(db).ReadAsync(db.CurrentStoreId!.Value, periods, false);
        var original = adjusted.DeliveryFragments.First(x => x.DeliveryOrderId == c.Id);
        adjusted.Entries.Add(new() { Id = adjusted.Entries.Max(x => x.Id) + 1, StoreId = adjusted.StoreId,
            EntryType = InventoryValuationEntryType.Revaluation, RevaluationOfEntryId = original.InventoryValuationEntryId });
        var adjustmentIssues = new List<ProfitCostIssueDto>();
        var adjustmentCosts = policy.EvaluateCosts(adjusted, issues: adjustmentIssues);
        Assert.Null(adjustmentCosts[sale.Id].Cost); Assert.Equal(ProfitQuality.Unavailable, adjustmentCosts[sale.Id].Quality);
        Assert.Contains(adjustmentIssues, x => x.OrderId == sale.Id && x.Message.Contains("điều chỉnh sau bàn giao"));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(before, await c.SnapshotAsync());
    }
}
