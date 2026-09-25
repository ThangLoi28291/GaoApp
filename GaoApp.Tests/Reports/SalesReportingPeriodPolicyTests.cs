using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Application.Services.Reports;

namespace GaoApp.Tests.Reports;

public sealed class SalesReportingPeriodPolicyTests
{
    private readonly SalesReportingPeriodPolicy _policy = new();

    [Fact]
    public void Resolve_should_use_vietnam_local_day_and_previous_day_by_default()
    {
        var utcNow = new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc);

        var result = _policy.Resolve(
            new SalesExecutiveDashboardQueryDto(),
            utcNow);

        Assert.Equal(new DateTime(2026, 9, 7), result.Current.Period.FromDate);
        Assert.Equal(new DateTime(2026, 9, 7), result.Current.Period.ToDate);
        Assert.Equal(
            new DateTime(2026, 9, 6, 17, 0, 0, DateTimeKind.Utc),
            result.Current.Period.FromUtc);
        Assert.Equal(
            new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc),
            result.Current.Period.ToUtcExclusive);
        Assert.Equal(SalesTrendGranularities.Hour, result.Current.Period.Granularity);
        Assert.Equal(24, result.Current.Period.BucketCount);

        Assert.NotNull(result.Comparison);
        Assert.Equal(new DateTime(2026, 9, 6), result.Comparison!.Period.FromDate);
        Assert.Equal(new DateTime(2026, 9, 6), result.Comparison.Period.ToDate);
        Assert.Equal(
            new DateTime(2026, 9, 5, 17, 0, 0, DateTimeKind.Utc),
            result.Comparison.Period.FromUtc);
    }

    [Fact]
    public void Resolve_should_keep_previous_period_equal_duration()
    {
        var request = new SalesExecutiveDashboardQueryDto
        {
            FromDate = new DateTime(2026, 9, 1),
            ToDate = new DateTime(2026, 9, 3),
            Compare = SalesComparisonModes.PreviousPeriod,
            TerminalId = 5,
            CustomerState = SalesCustomerStates.Linked
        };

        var result = _policy.Resolve(
            request,
            new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 1), result.Current.Period.FromDate);
        Assert.Equal(new DateTime(2026, 9, 3), result.Current.Period.ToDate);
        Assert.Equal(3, result.Current.Period.BucketCount);
        Assert.Equal(SalesTrendGranularities.Day, result.Current.Period.Granularity);
        Assert.Equal(5, result.Current.TerminalId);
        Assert.Equal(SalesCustomerStates.Linked, result.Current.CustomerState);

        Assert.NotNull(result.Comparison);
        Assert.Equal(new DateTime(2026, 8, 29), result.Comparison!.Period.FromDate);
        Assert.Equal(new DateTime(2026, 8, 31), result.Comparison.Period.ToDate);
        Assert.Equal(result.Current.Period.BucketCount, result.Comparison.Period.BucketCount);
    }

    [Fact]
    public void Resolve_should_support_no_comparison_and_normalize_secondary_filters()
    {
        var result = _policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                Compare = SalesComparisonModes.None,
                TerminalId = 0,
                CustomerState = "UNKNOWN"
            },
            new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc));

        Assert.Equal(SalesComparisonModes.None, result.ComparisonMode);
        Assert.Null(result.Comparison);
        Assert.Null(result.Current.TerminalId);
        Assert.Equal(SalesCustomerStates.All, result.Current.CustomerState);
    }

    [Fact]
    public void Resolve_should_reject_invalid_or_oversized_range()
    {
        Assert.Throws<ArgumentException>(() => _policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2026, 9, 8),
                ToDate = new DateTime(2026, 9, 7)
            },
            new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc)));

        Assert.Throws<ArgumentOutOfRangeException>(() => _policy.Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = new DateTime(2025, 1, 1),
                ToDate = new DateTime(2026, 1, 2)
            },
            new DateTime(2026, 9, 7, 7, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void ResolveDetail_should_apply_hour_bucket_and_bound_pagination()
    {
        var policy = new SalesReportingPeriodPolicy();
        var resolved = policy.ResolveDetail(
            new SalesDetailQueryDto
            {
                FromDate = new DateTime(2026, 9, 7),
                ToDate = new DateTime(2026, 9, 7),
                BucketIndex = 8,
                Page = 0,
                PageSize = 500
            },
            new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc));

        Assert.True(resolved.BucketIndex.HasValue);
        Assert.Equal(8, resolved.BucketIndex.Value);
        Assert.Equal(1, resolved.Page);
        Assert.Equal(100, resolved.PageSize);
        Assert.Equal(TimeSpan.FromHours(1), resolved.EffectiveToUtcExclusive - resolved.EffectiveFromUtc);
    }

}
