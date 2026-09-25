using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Interfaces.Repositories.Reports;
using GaoApp.Application.Interfaces.Services.Reports;

namespace GaoApp.Application.Services.Reports;

public sealed class SalesReportReadService : ISalesReportReadService
{
    private readonly ISalesReportReadRepository _repository;
    private readonly ICurrentStore _currentStore;
    private readonly SalesReportingPeriodPolicy _periodPolicy;

    public SalesReportReadService(
        ISalesReportReadRepository repository,
        ICurrentStore currentStore,
        SalesReportingPeriodPolicy periodPolicy)
    {
        _repository = repository;
        _currentStore = currentStore;
        _periodPolicy = periodPolicy;
    }

    public async Task<SalesExecutiveDashboardDto> GetExecutiveDashboardAsync(
        SalesExecutiveDashboardQueryDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var generatedAtUtc = DateTime.UtcNow;
        var resolved = _periodPolicy.Resolve(request, generatedAtUtc);
        var storeId = _currentStore.StoreId;

        if (storeId <= 0)
            throw new InvalidOperationException("Store hiện tại chưa được resolve hợp lệ.");

        var currentRaw = await _repository.GetExecutivePeriodAsync(
            storeId,
            resolved.Current,
            ct);

        var current = NormalizePeriodData(
            currentRaw,
            resolved.Current.Period);

        SalesExecutivePeriodDataDto? comparison = null;
        if (resolved.Comparison is not null)
        {
            var comparisonRaw = await _repository.GetExecutivePeriodAsync(
                storeId,
                resolved.Comparison,
                ct);

            comparison = NormalizePeriodData(
                comparisonRaw,
                resolved.Comparison.Period);
        }

        var terminals = await _repository.GetTerminalOptionsAsync(storeId, ct);

        return new SalesExecutiveDashboardDto
        {
            GeneratedAtUtc = generatedAtUtc,
            Query = new SalesExecutiveDashboardQueryDto
            {
                FromDate = resolved.Current.Period.FromDate,
                ToDate = resolved.Current.Period.ToDate,
                Compare = resolved.ComparisonMode,
                TerminalId = resolved.Current.TerminalId,
                CustomerState = resolved.Current.CustomerState
            },
            CurrentPeriod = resolved.Current.Period,
            ComparisonPeriod = resolved.Comparison?.Period,
            Current = current,
            Comparison = comparison,
            Comparisons = BuildComparisons(
                current.Summary,
                comparison?.Summary),
            Terminals = terminals
        };
    }

    public async Task<SalesDetailContextDto> GetDetailContextAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var generatedAtUtc = DateTime.UtcNow;
        var resolved = _periodPolicy.ResolveDetail(request, generatedAtUtc);
        var storeId = RequireStoreId();
        var terminals = await _repository.GetTerminalOptionsAsync(storeId, ct);

        return new SalesDetailContextDto
        {
            GeneratedAtUtc = generatedAtUtc,
            Query = ToDetailQueryDto(resolved),
            Period = resolved.Period,
            EffectiveFromUtc = resolved.EffectiveFromUtc,
            EffectiveToUtcExclusive = resolved.EffectiveToUtcExclusive,
            BucketLabel = resolved.BucketIndex.HasValue
                ? _periodPolicy.FormatBucketLabel(resolved.Period, resolved.BucketIndex.Value)
                : null,
            Terminals = terminals
        };
    }

    public async Task<SalesPagedResultDto<SalesOrderDetailRowDto>> GetOrdersAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        var resolved = _periodPolicy.ResolveDetail(request, DateTime.UtcNow);
        var result = await _repository.GetOrdersAsync(RequireStoreId(), resolved, ct);
        foreach (var row in result.Items)
        {
            row.CompletedAtLocal = _periodPolicy.ConvertUtcToLocal(row.CompletedAtUtc);
            if (string.IsNullOrWhiteSpace(row.OrderNumber)) row.OrderNumber = $"#{row.OrderId}";
            row.StatusLabel = row.StatusCode switch
            {
                2 => "Hoàn tất",
                5 => "Đã hoàn/trả",
                _ => "Đã bán"
            };
        }
        return result;
    }

    public Task<SalesPagedResultDto<SalesProductDetailRowDto>> GetProductsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        var resolved = _periodPolicy.ResolveDetail(request, DateTime.UtcNow);
        return _repository.GetProductsAsync(RequireStoreId(), resolved, ct);
    }

    public async Task<SalesPagedResultDto<SalesDiscountOrderRowDto>> GetDiscountsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        var resolved = _periodPolicy.ResolveDetail(request, DateTime.UtcNow);
        var result = await _repository.GetDiscountsAsync(RequireStoreId(), resolved, ct);
        foreach (var row in result.Items)
        {
            row.CompletedAtLocal = _periodPolicy.ConvertUtcToLocal(row.CompletedAtUtc);
            if (string.IsNullOrWhiteSpace(row.OrderNumber)) row.OrderNumber = $"#{row.OrderId}";
            var componentTotal = row.ManualLine + row.Promotion + row.Combo + row.ManualOrder + row.Voucher;
            row.ReconciliationDifference = row.TotalDiscounts - componentTotal;
            row.IsReconciled = row.ReconciliationDifference == 0m;
        }
        return result;
    }

    public async Task<SalesPagedResultDto<SalesReturnDetailRowDto>> GetReturnsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        var resolved = _periodPolicy.ResolveDetail(request, DateTime.UtcNow);
        var result = await _repository.GetReturnsAsync(RequireStoreId(), resolved, ct);
        foreach (var row in result.Items)
        {
            row.CompletedAtLocal = _periodPolicy.ConvertUtcToLocal(row.CompletedAtUtc);
            if (string.IsNullOrWhiteSpace(row.OrderNumber)) row.OrderNumber = $"#{row.OrderId}";
            row.TypeLabel = row.TypeCode switch
            {
                1 => "Chỉ hoàn tiền",
                2 => "Chỉ trả hàng",
                3 => "Trả hàng & hoàn tiền",
                4 => "Đổi hàng",
                _ => "Khác"
            };
        }
        return result;
    }

    public async Task<SalesPagedResultDto<SalesVoidDetailRowDto>> GetVoidsAsync(
        SalesDetailQueryDto request,
        CancellationToken ct = default)
    {
        var resolved = _periodPolicy.ResolveDetail(request, DateTime.UtcNow);
        var result = await _repository.GetVoidsAsync(RequireStoreId(), resolved, ct);
        foreach (var row in result.Items)
        {
            row.CompletedAtLocal = _periodPolicy.ConvertUtcToLocal(row.CompletedAtUtc);
            if (string.IsNullOrWhiteSpace(row.OrderNumber)) row.OrderNumber = $"#{row.OrderId}";
        }
        return result;
    }

    private int RequireStoreId()
    {
        var storeId = _currentStore.StoreId;
        if (storeId <= 0)
            throw new InvalidOperationException("Store hiện tại chưa được resolve hợp lệ.");
        return storeId;
    }

    private static SalesDetailQueryDto ToDetailQueryDto(SalesResolvedDetailQueryDto resolved)
        => new()
        {
            FromDate = resolved.Period.FromDate,
            ToDate = resolved.Period.ToDate,
            TerminalId = resolved.TerminalId,
            CustomerState = resolved.CustomerState,
            Search = resolved.Search,
            Page = resolved.Page,
            PageSize = resolved.PageSize,
            Sort = resolved.Sort,
            VariantId = resolved.VariantId,
            BucketIndex = resolved.BucketIndex,
            Focus = resolved.Focus
        };

    private SalesExecutivePeriodDataDto NormalizePeriodData(
        SalesExecutivePeriodDataDto raw,
        SalesReportPeriodDto period)
    {
        raw ??= new SalesExecutivePeriodDataDto();
        raw.Summary ??= new SalesMetricSummaryDto();
        raw.Trend ??= new List<SalesTrendPointDto>();
        raw.SalesByHour ??= new List<SalesByHourPointDto>();
        raw.TopProducts ??= new List<SalesTopProductDto>();
        raw.DiscountBreakdown ??= new SalesDiscountBreakdownDto();

        var summary = raw.Summary;
        summary.NetSales = summary.SalesAfterDiscount - summary.Returns;
        summary.Aov = summary.SalesOrders > 0
            ? Math.Round(
                summary.SalesAfterDiscount / summary.SalesOrders,
                2,
                MidpointRounding.AwayFromZero)
            : 0m;
        summary.CustomerLinkedRate = summary.SalesOrders > 0
            ? Math.Round(
                summary.CustomerLinkedOrders * 100m / summary.SalesOrders,
                1,
                MidpointRounding.AwayFromZero)
            : 0m;

        var completedTrend = CompleteTrend(raw.Trend, period);
        var completedSalesByHour = CompleteSalesByHour(raw.SalesByHour);
        var topProducts = NormalizeTopProducts(raw.TopProducts, summary.GrossSales);
        var discountBreakdown = NormalizeDiscountBreakdown(
            raw.DiscountBreakdown,
            summary.Discounts);
        var customerMix = BuildCustomerMix(summary);

        return new SalesExecutivePeriodDataDto
        {
            Summary = summary,
            Bridge = new SalesBridgeDto
            {
                GrossSales = summary.GrossSales,
                Discounts = summary.Discounts,
                SalesAfterDiscount = summary.SalesAfterDiscount,
                Returns = summary.Returns,
                NetSales = summary.NetSales
            },
            Trend = completedTrend,
            SalesByHour = completedSalesByHour,
            TopProducts = topProducts,
            DiscountBreakdown = discountBreakdown,
            CustomerMix = customerMix
        };
    }

    private List<SalesTrendPointDto> CompleteTrend(
        IEnumerable<SalesTrendPointDto> rawTrend,
        SalesReportPeriodDto period)
    {
        var rawByBucket = rawTrend
            .GroupBy(x => x.BucketIndex)
            .ToDictionary(
                g => g.Key,
                g => new SalesTrendPointDto
                {
                    BucketIndex = g.Key,
                    GrossSales = g.Sum(x => x.GrossSales),
                    Discounts = g.Sum(x => x.Discounts),
                    SalesAfterDiscount = g.Sum(x => x.SalesAfterDiscount),
                    Returns = g.Sum(x => x.Returns),
                    RefundAmount = g.Sum(x => x.RefundAmount),
                    SalesOrders = g.Sum(x => x.SalesOrders),
                    ReturnCount = g.Sum(x => x.ReturnCount),
                    RefundCount = g.Sum(x => x.RefundCount)
                });

        var completedTrend = new List<SalesTrendPointDto>(period.BucketCount);
        for (var bucketIndex = 0; bucketIndex < period.BucketCount; bucketIndex++)
        {
            rawByBucket.TryGetValue(bucketIndex, out var point);
            point ??= new SalesTrendPointDto { BucketIndex = bucketIndex };

            point.BucketLocalStart = _periodPolicy.GetBucketLocalStart(
                period,
                bucketIndex);
            point.Label = _periodPolicy.FormatBucketLabel(
                period,
                bucketIndex);
            point.NetSales = point.SalesAfterDiscount - point.Returns;

            completedTrend.Add(point);
        }

        return completedTrend;
    }

    private static List<SalesByHourPointDto> CompleteSalesByHour(
        IEnumerable<SalesByHourPointDto> rawPoints)
    {
        var byHour = rawPoints
            .Where(x => x.Hour >= 0 && x.Hour <= 23)
            .GroupBy(x => x.Hour)
            .ToDictionary(
                g => g.Key,
                g => new SalesByHourPointDto
                {
                    Hour = g.Key,
                    SalesAfterDiscount = g.Sum(x => x.SalesAfterDiscount),
                    Returns = g.Sum(x => x.Returns),
                    SalesOrders = g.Sum(x => x.SalesOrders)
                });

        var result = new List<SalesByHourPointDto>(24);
        for (var hour = 0; hour < 24; hour++)
        {
            byHour.TryGetValue(hour, out var point);
            point ??= new SalesByHourPointDto { Hour = hour };
            point.Label = $"{hour:00}:00";
            point.NetSales = point.SalesAfterDiscount - point.Returns;
            result.Add(point);
        }

        return result;
    }

    private static List<SalesTopProductDto> NormalizeTopProducts(
        IEnumerable<SalesTopProductDto> rawProducts,
        decimal periodGrossSales)
        => rawProducts
            .OrderByDescending(x => x.GrossSales)
            .ThenBy(x => x.ItemName)
            .Take(10)
            .Select(x => new SalesTopProductDto
            {
                VariantId = x.VariantId,
                ItemName = x.ItemName,
                Sku = x.Sku,
                BaseUnitName = string.IsNullOrWhiteSpace(x.BaseUnitName)
                    ? "Đơn vị gốc"
                    : x.BaseUnitName,
                BaseQuantity = x.BaseQuantity,
                GrossSales = x.GrossSales,
                GrossSalesShare = periodGrossSales > 0m
                    ? Math.Round(
                        x.GrossSales * 100m / periodGrossSales,
                        1,
                        MidpointRounding.AwayFromZero)
                    : 0m
            })
            .ToList();

    private static SalesDiscountBreakdownDto NormalizeDiscountBreakdown(
        SalesDiscountBreakdownDto raw,
        decimal totalDiscounts)
    {
        var componentTotal = raw.ManualLine
            + raw.Promotion
            + raw.Combo
            + raw.ManualOrder
            + raw.Voucher;
        var difference = totalDiscounts - componentTotal;

        return new SalesDiscountBreakdownDto
        {
            ManualLine = raw.ManualLine,
            Promotion = raw.Promotion,
            Combo = raw.Combo,
            ManualOrder = raw.ManualOrder,
            Voucher = raw.Voucher,
            TotalDiscounts = totalDiscounts,
            ComponentTotal = componentTotal,
            ReconciliationDifference = difference,
            IsReconciled = difference == 0m
        };
    }

    private static SalesCustomerMixDto BuildCustomerMix(
        SalesMetricSummaryDto summary)
    {
        var linked = summary.CustomerLinkedOrders;
        var guest = Math.Max(0, summary.SalesOrders - linked);

        return new SalesCustomerMixDto
        {
            LinkedOrders = linked,
            GuestOrders = guest,
            LinkedRate = summary.CustomerLinkedRate,
            GuestRate = summary.SalesOrders > 0
                ? Math.Round(
                    guest * 100m / summary.SalesOrders,
                    1,
                    MidpointRounding.AwayFromZero)
                : 0m
        };
    }

    private static SalesExecutiveComparisonsDto BuildComparisons(
        SalesMetricSummaryDto current,
        SalesMetricSummaryDto? comparison)
    {
        var previous = comparison ?? new SalesMetricSummaryDto();

        return new SalesExecutiveComparisonsDto
        {
            NetSales = BuildDelta(current.NetSales, previous.NetSales),
            SalesOrders = BuildDelta(current.SalesOrders, previous.SalesOrders),
            Aov = BuildDelta(current.Aov, previous.Aov),
            Discounts = BuildDelta(current.Discounts, previous.Discounts),
            Returns = BuildDelta(current.Returns, previous.Returns),
            RefundAmount = BuildDelta(current.RefundAmount, previous.RefundAmount)
        };
    }

    private static SalesMetricDeltaDto BuildDelta(
        decimal current,
        decimal previous)
    {
        var difference = current - previous;
        decimal? percentChange = null;
        string state;

        if (previous == 0m)
        {
            state = current == 0m
                ? SalesMetricDeltaStates.Flat
                : SalesMetricDeltaStates.New;
        }
        else
        {
            percentChange = Math.Round(
                difference / Math.Abs(previous) * 100m,
                1,
                MidpointRounding.AwayFromZero);

            state = difference switch
            {
                > 0m => SalesMetricDeltaStates.Up,
                < 0m => SalesMetricDeltaStates.Down,
                _ => SalesMetricDeltaStates.Flat
            };
        }

        return new SalesMetricDeltaDto
        {
            Current = current,
            Comparison = previous,
            Difference = difference,
            PercentChange = percentChange,
            State = state
        };
    }
}
