using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Services.Reports;

namespace GaoApp.Tests.Reports;

public sealed class SalesReportMetricReconciliationTests
{
    [Fact]
    public async Task Service_should_reconcile_bridge_discount_breakdown_hourly_customer_mix_and_top_product_share()
    {
        var repository = new FakeSalesReportReadRepository();
        var service = new SalesReportReadService(
            repository,
            new TestCurrentStore(7),
            new SalesReportingPeriodPolicy());

        var result = await service.GetExecutiveDashboardAsync(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.PreviousPeriod
            });

        Assert.Equal(1000m, result.Current.Summary.GrossSales);
        Assert.Equal(120m, result.Current.Summary.Discounts);
        Assert.Equal(880m, result.Current.Summary.SalesAfterDiscount);
        Assert.Equal(80m, result.Current.Summary.Returns);
        Assert.Equal(800m, result.Current.Summary.NetSales);
        Assert.Equal(60m, result.Current.Summary.RefundAmount);
        Assert.Equal(2, result.Current.Summary.SalesOrders);
        Assert.Equal(440m, result.Current.Summary.Aov);
        Assert.Equal(50m, result.Current.Summary.CustomerLinkedRate);

        Assert.Equal(result.Current.Summary.GrossSales, result.Current.Bridge.GrossSales);
        Assert.Equal(result.Current.Summary.Discounts, result.Current.Bridge.Discounts);
        Assert.Equal(result.Current.Summary.SalesAfterDiscount, result.Current.Bridge.SalesAfterDiscount);
        Assert.Equal(result.Current.Summary.Returns, result.Current.Bridge.Returns);
        Assert.Equal(result.Current.Summary.NetSales, result.Current.Bridge.NetSales);

        Assert.True(result.Current.DiscountBreakdown.IsReconciled);
        Assert.Equal(120m, result.Current.DiscountBreakdown.ComponentTotal);
        Assert.Equal(0m, result.Current.DiscountBreakdown.ReconciliationDifference);

        Assert.Equal(24, result.Current.SalesByHour.Count);
        var hour8 = Assert.Single(result.Current.SalesByHour.Where(x => x.Hour == 8));
        Assert.Equal("08:00", hour8.Label);
        Assert.Equal(380m, hour8.NetSales);

        Assert.Equal(1, result.Current.CustomerMix.LinkedOrders);
        Assert.Equal(1, result.Current.CustomerMix.GuestOrders);
        Assert.Equal(50m, result.Current.CustomerMix.LinkedRate);
        Assert.Equal(50m, result.Current.CustomerMix.GuestRate);

        var top = Assert.Single(result.Current.TopProducts);
        Assert.Equal(500m, top.GrossSales);
        Assert.Equal(50m, top.GrossSalesShare);
        Assert.Equal("chai", top.BaseUnitName);

        Assert.Equal(SalesMetricDeltaStates.New, result.Comparisons.NetSales.State);
        Assert.Null(result.Comparisons.NetSales.PercentChange);
        Assert.Equal(800m, result.Comparisons.NetSales.Difference);

        Assert.Equal(24, result.Current.Trend.Count);
        Assert.All(result.Current.Trend, point => Assert.NotEmpty(point.Label));
        Assert.Equal(60m, result.Current.Trend.Sum(x => x.RefundAmount));
    }

    [Fact]
    public async Task Service_should_surface_discount_reconciliation_difference_without_double_counting_total()
    {
        var service = new SalesReportReadService(
            new FakeSalesReportReadRepository(discountComponentOverride: 110m),
            new TestCurrentStore(1),
            new SalesReportingPeriodPolicy());

        var result = await service.GetExecutiveDashboardAsync(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None
            });

        Assert.Equal(120m, result.Current.Summary.Discounts);
        Assert.Equal(120m, result.Current.DiscountBreakdown.TotalDiscounts);
        Assert.Equal(110m, result.Current.DiscountBreakdown.ComponentTotal);
        Assert.Equal(10m, result.Current.DiscountBreakdown.ReconciliationDifference);
        Assert.False(result.Current.DiscountBreakdown.IsReconciled);
    }

    [Fact]
    public async Task Service_should_not_create_infinity_or_nan_when_previous_period_is_zero()
    {
        var service = new SalesReportReadService(
            new FakeSalesReportReadRepository(),
            new TestCurrentStore(1),
            new SalesReportingPeriodPolicy());

        var result = await service.GetExecutiveDashboardAsync(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.PreviousPeriod
            });

        Assert.Null(result.Comparisons.NetSales.PercentChange);
        Assert.Null(result.Comparisons.Aov.PercentChange);
        Assert.Equal(SalesMetricDeltaStates.New, result.Comparisons.SalesOrders.State);
    }

    private sealed class FakeSalesReportReadRepository : ISalesReportReadRepository
    {
        private readonly decimal? _discountComponentOverride;

        public FakeSalesReportReadRepository(decimal? discountComponentOverride = null)
        {
            _discountComponentOverride = discountComponentOverride;
        }

        public Task<SalesExecutivePeriodDataDto> GetExecutivePeriodAsync(
            int storeId,
            SalesResolvedExecutiveQueryDto query,
            CancellationToken ct = default)
        {
            Assert.True(storeId > 0);

            var isCurrent = query.Period.FromDate.Date == new DateTime(2026, 9, 7);
            if (!isCurrent)
            {
                return Task.FromResult(new SalesExecutivePeriodDataDto());
            }

            var componentTotal = _discountComponentOverride ?? 120m;

            return Task.FromResult(new SalesExecutivePeriodDataDto
            {
                Summary = new SalesMetricSummaryDto
                {
                    GrossSales = 1000m,
                    Discounts = 120m,
                    SalesAfterDiscount = 880m,
                    Returns = 80m,
                    RefundAmount = 60m,
                    SalesOrders = 2,
                    ReturnCount = 1,
                    RefundCount = 1,
                    VoidCount = 1,
                    VoidValue = 150m,
                    CustomerLinkedOrders = 1
                },
                DiscountBreakdown = new SalesDiscountBreakdownDto
                {
                    ManualLine = componentTotal,
                    TotalDiscounts = 120m
                },
                SalesByHour = new List<SalesByHourPointDto>
                {
                    new()
                    {
                        Hour = 8,
                        SalesAfterDiscount = 460m,
                        Returns = 80m,
                        SalesOrders = 1
                    }
                },
                TopProducts = new List<SalesTopProductDto>
                {
                    new()
                    {
                        VariantId = 101,
                        ItemName = "Nước A",
                        Sku = "A-101",
                        BaseUnitName = "chai",
                        BaseQuantity = 10m,
                        GrossSales = 500m
                    }
                },
                Trend = new List<SalesTrendPointDto>
                {
                    new()
                    {
                        BucketIndex = 8,
                        GrossSales = 1000m,
                        Discounts = 120m,
                        SalesAfterDiscount = 880m,
                        Returns = 80m,
                        RefundAmount = 60m,
                        SalesOrders = 2,
                        ReturnCount = 1,
                        RefundCount = 1
                    }
                }
            });
        }

        public Task<SalesPagedResultDto<SalesOrderDetailRowDto>> GetOrdersAsync(int storeId, SalesResolvedDetailQueryDto query, CancellationToken ct = default)
            => Task.FromResult(new SalesPagedResultDto<SalesOrderDetailRowDto>());
        public Task<SalesPagedResultDto<SalesProductDetailRowDto>> GetProductsAsync(int storeId, SalesResolvedDetailQueryDto query, CancellationToken ct = default)
            => Task.FromResult(new SalesPagedResultDto<SalesProductDetailRowDto>());
        public Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> GetDiscountsAsync(int storeId, SalesResolvedDetailQueryDto query, CancellationToken ct = default)
            => Task.FromResult(new SalesPagedResultDto<SalesDiscountOrderRowDto>());
        public Task<SalesPagedResultDto<SalesReturnDetailRowDto>> GetReturnsAsync(int storeId, SalesResolvedDetailQueryDto query, CancellationToken ct = default)
            => Task.FromResult(new SalesPagedResultDto<SalesReturnDetailRowDto>());
        public Task<SalesPagedResultDto<SalesVoidDetailRowDto>> GetVoidsAsync(int storeId, SalesResolvedDetailQueryDto query, CancellationToken ct = default)
            => Task.FromResult(new SalesPagedResultDto<SalesVoidDetailRowDto>());

        public Task<List<SalesTerminalOptionDto>> GetTerminalOptionsAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult(new List<SalesTerminalOptionDto>());
    }

    private sealed class TestCurrentStore : ICurrentStore
    {
        public TestCurrentStore(int storeId)
        {
            StoreId = storeId;
        }

        public int StoreId { get; }
    }
}
