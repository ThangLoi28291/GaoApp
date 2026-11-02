using System.Text.Json;
using GaoApp.Application.DTOs.Reports;
using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Application.Services.Reports;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Reports;

public sealed class ManagementReportOverviewTests
{
    [Fact]
    public async Task Service_overview_preserves_full_financial_metrics_and_rankings_for_both_periods()
    {
        var source = ProfitReportAggregationPolicyTests.Snapshot();
        source.Orders[0].CustomerId = 1; source.Customers.Add(new(1, "Customer 1", "RETAIL"));
        source.Products.Add(new(1, "Product 1", "SKU-1", 1, "Category 1"));
        for (var i = 2; i <= 160; i++)
        {
            source.Orders.Add(new Order { Id = i, StoreId = 1, Status = OrderStatus.Completed,
                OrderNumber = "POS-" + i, CompletedAtUtc = ProfitReportAggregationPolicyTests.At.AddDays(i > 80 ? -1 : 0),
                POSShiftId = 1, Subtotal = i * 10, CustomerId = i % 7 == 0 ? null : i,
                CustomerPriceTierSnapshot = i % 3 == 0 ? "WHOLESALE" : "RETAIL" });
            source.Lines.Add(new OrderLine { Id = i, StoreId = 1, OrderId = i, VariantId = i,
                BaseQuantity = 1, Quantity = 1, Multiplier = 1, ItemName = "Product " + i, LineTotal = i * 10 });
            source.Customers.Add(new(i, "Customer " + i, "RETAIL"));
            source.Products.Add(new(i, "Product " + i, "SKU-" + i, i % 12, "Category " + i % 12));
        }
        var periods = new SalesReportingPeriodPolicy(); using var gate = new ProfitReportExecutionGate(new());
        var service = new ManagementReportService(new SourceReader(source), new Store(), periods, new(periods), gate);
        var query = new ManagementReportQueryDto { FromDate = ProfitReportAggregationPolicyTests.At.Date,
            ToDate = ProfitReportAggregationPolicyTests.At.Date, Compare = "previous" };
        var full = await service.ReadAsync(query); var compact = await service.ReadOverviewAsync(query);
        Assert.Equal(JsonSerializer.Serialize(full.Current.Summary), JsonSerializer.Serialize(compact.Current.Summary));
        Assert.Equal(JsonSerializer.Serialize(full.Comparison!.Summary), JsonSerializer.Serialize(compact.Comparison!.Summary));
        Assert.Equal(JsonSerializer.Serialize(full.Current.Products.Where(x => x.Summary.NetSales.Available).Take(6)), JsonSerializer.Serialize(compact.Current.Products));
        Assert.Equal(JsonSerializer.Serialize(full.Current.Customers.Where(x => x.Id != 0).Take(5)), JsonSerializer.Serialize(compact.Current.Customers));
        foreach (var pair in new[] { (full.Current, compact.Current), (full.Comparison!, compact.Comparison!) })
        {
            Assert.Equal(JsonSerializer.Serialize(pair.Item1.Trend), JsonSerializer.Serialize(pair.Item2.Trend));
            Assert.Equal(JsonSerializer.Serialize(pair.Item1.CustomerGroups), JsonSerializer.Serialize(pair.Item2.CustomerGroups));
            Assert.Equal(JsonSerializer.Serialize(pair.Item1.CostDiagnostics), JsonSerializer.Serialize(pair.Item2.CostDiagnostics));
            Assert.Equal(pair.Item1.SalesOrders, pair.Item2.SalesOrders);
            Assert.Equal(pair.Item1.OperatingProfit, pair.Item2.OperatingProfit);
        }
        Assert.Empty(compact.Current.Categories); Assert.Empty(compact.Comparison.Products); Assert.Empty(compact.Comparison.Customers);
        Assert.Equal(80, full.Current.Products.Count); Assert.Equal(80, full.Comparison.Products.Count);
    }

    private sealed class Store : ICurrentStore { public int StoreId => 1; }
    private sealed class SourceReader(ProfitSourceSnapshot source) : IProfitReportReadRepository
    {
        public Task<ProfitSourceSnapshot> ReadAsync(int storeId, SalesResolvedPeriodSet periods, bool activity, CancellationToken ct = default)
            => Task.FromResult(source);
    }
    [Fact]
    public async Task Overview_keeps_complete_totals_trends_and_quality_without_sending_unused_dimension_rows()
    {
        var summary = ProfitReportAggregationPolicy.Summarize(100_000,
            [new(40_000, 2_000, ProfitQuality.Provisional, 3)], true);
        var rows = Enumerable.Range(0, 1500).Select(i => new ReportDimensionDto(i, "Sản phẩm / khách hàng " + i,
            "RETAIL", "SKU-" + i, 10, 4, summary)).ToList();
        var period = new ManagementPeriodDto { Summary = summary, OperatingExpenses = 3_000,
            OperatingProfit = 57_000, SalesOrders = 700, InferredCustomerOrders = 9, DraftExpenses = 2,
            Trend = [new(0, "09:00", summary)], ExpenseTrend = [3000], GroupTrend = [new(0, "09:00", 1000, 99000, 0)],
            Products = rows, Categories = rows, Customers = rows,
            CustomerGroups = [rows[1]], ExpenseCategories = [new("rent", "Mặt bằng", 3000)] };
        var original = new ManagementReportDto { Current = period, Comparison = period };
        var controller = new ManagementReportController(new Report(original));
        var result = Assert.IsType<ManagementReportDto>(Assert.IsType<OkObjectResult>(
            await controller.GetData(new(), default, "overview")).Value);

        foreach (var compact in new[] { result.Current, result.Comparison! })
        {
            Assert.Same(summary, compact.Summary);
            Assert.Equal(period.SalesOrders, compact.SalesOrders);
            Assert.Equal(period.OperatingProfit, compact.OperatingProfit);
            Assert.Equal(period.OperatingExpenses, compact.OperatingExpenses);
            Assert.Equal(period.InferredCustomerOrders, compact.InferredCustomerOrders);
            Assert.Equal(period.DraftExpenses, compact.DraftExpenses);
            Assert.Equal(period.Trend, compact.Trend); Assert.Equal(period.GroupTrend, compact.GroupTrend);
            Assert.Equal(period.ExpenseTrend, compact.ExpenseTrend);
            Assert.Equal(period.CustomerGroups, compact.CustomerGroups);
            Assert.Equal(period.ExpenseCategories, compact.ExpenseCategories);
            Assert.Empty(compact.Categories);
        }
        Assert.Equal(rows.Take(6), result.Current.Products);
        Assert.Equal(rows.Where(x => x.Id != 0).Take(5), result.Current.Customers);
        Assert.Empty(result.Comparison!.Products); Assert.Empty(result.Comparison.Customers);
        Assert.True(JsonSerializer.Serialize(result).Length < JsonSerializer.Serialize(original).Length / 10);
        // The overview must not mutate the full report used by other views or export callers.
        var full = Assert.IsType<ManagementReportDto>(Assert.IsType<OkObjectResult>(
            await controller.GetData(new(), default)).Value);
        Assert.Same(original, full); Assert.Equal(1500, full.Current.Products.Count);
        Assert.Equal(1500, full.Comparison!.Customers.Count);
        Assert.IsType<BadRequestObjectResult>(await controller.GetData(new(), default, "unknown"));
    }

    [Fact]
    public async Task Overview_ranking_retains_visible_products_after_unavailable_revenue_and_supports_no_comparison()
    {
        var missing = new ProfitSummaryDto();
        var known = ProfitReportAggregationPolicy.Summarize(100, [], true);
        var period = new ManagementPeriodDto { Products = Enumerable.Range(1, 8)
            .Select(i => new ReportDimensionDto(i, "Product " + i, null, null, 1, 1, i < 3 ? missing : known)).ToList() };
        var controller = new ManagementReportController(new Report(new() { Current = period }));
        var result = Assert.IsType<ManagementReportDto>(Assert.IsType<OkObjectResult>(
            await controller.GetData(new(), default, "overview")).Value);
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8 }, result.Current.Products.Select(x => x.Id));
        Assert.Null(result.Comparison);
    }

    private sealed class Report(ManagementReportDto report) : IManagementReportService
    {
        public Task<ManagementReportDto> ReadAsync(ManagementReportQueryDto query, CancellationToken ct = default) => Task.FromResult(report);
    }
}
