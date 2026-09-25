using GaoApp.Application.DTOs.Reports.Sales;

namespace GaoApp.Application.Services.Reports;

public sealed class SalesReportingPeriodPolicy
{
    public const int MaximumInteractiveDays = 366;
    public const string CanonicalTimeZoneId = "Asia/Ho_Chi_Minh";
    public const string WindowsFallbackTimeZoneId = "SE Asia Standard Time";

    private readonly TimeZoneInfo _timeZone;

    public SalesReportingPeriodPolicy()
        : this(ResolveTimeZone())
    {
    }

    internal SalesReportingPeriodPolicy(TimeZoneInfo timeZone)
    {
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    public SalesResolvedPeriodSet Resolve(
        SalesExecutiveDashboardQueryDto request,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedUtcNow = EnsureUtc(utcNow);
        var localToday = TimeZoneInfo.ConvertTimeFromUtc(
            normalizedUtcNow,
            _timeZone).Date;

        var fromDate = request.FromDate?.Date
            ?? request.ToDate?.Date
            ?? localToday;

        var toDate = request.ToDate?.Date
            ?? request.FromDate?.Date
            ?? localToday;

        if (fromDate > toDate)
        {
            throw new ArgumentException(
                "Ngày bắt đầu không được lớn hơn ngày kết thúc.",
                nameof(request));
        }

        var dayCount = (toDate - fromDate).Days + 1;
        if (dayCount > MaximumInteractiveDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"Dashboard chỉ hỗ trợ tối đa {MaximumInteractiveDays} ngày cho một lần xem.");
        }

        var terminalId = request.TerminalId is > 0
            ? request.TerminalId
            : null;

        var customerState = NormalizeCustomerState(request.CustomerState);
        var comparisonMode = NormalizeComparisonMode(request.Compare);

        var current = new SalesResolvedExecutiveQueryDto
        {
            Period = BuildPeriod(fromDate, toDate),
            TerminalId = terminalId,
            CustomerState = customerState
        };

        SalesResolvedExecutiveQueryDto? comparison = null;
        if (comparisonMode == SalesComparisonModes.PreviousPeriod)
        {
            var compareToDate = fromDate.AddDays(-1);
            var compareFromDate = compareToDate.AddDays(-(dayCount - 1));

            comparison = new SalesResolvedExecutiveQueryDto
            {
                Period = BuildPeriod(compareFromDate, compareToDate),
                TerminalId = terminalId,
                CustomerState = customerState
            };
        }

        return new SalesResolvedPeriodSet
        {
            ComparisonMode = comparisonMode,
            Current = current,
            Comparison = comparison
        };
    }

    public DateTime GetBucketLocalStart(
        SalesReportPeriodDto period,
        int bucketIndex)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (bucketIndex < 0 || bucketIndex >= period.BucketCount)
            throw new ArgumentOutOfRangeException(nameof(bucketIndex));

        var bucketUtc = period.FromUtc.AddHours(
            (long)bucketIndex * period.BucketHours);

        return TimeZoneInfo.ConvertTimeFromUtc(bucketUtc, _timeZone);
    }

    public string FormatBucketLabel(
        SalesReportPeriodDto period,
        int bucketIndex)
    {
        var localStart = GetBucketLocalStart(period, bucketIndex);

        return period.Granularity switch
        {
            SalesTrendGranularities.Hour => localStart.ToString("HH:mm"),
            SalesTrendGranularities.Week => localStart.ToString("dd/MM"),
            _ => localStart.ToString("dd/MM")
        };
    }

    public SalesResolvedDetailQueryDto ResolveDetail(
        SalesDetailQueryDto request,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(request);

        var periodSet = Resolve(
            new SalesExecutiveDashboardQueryDto
            {
                FromDate = request.FromDate,
                ToDate = request.ToDate,
                Compare = SalesComparisonModes.None,
                TerminalId = request.TerminalId,
                CustomerState = request.CustomerState
            },
            utcNow);

        var period = periodSet.Current.Period;
        var effectiveFromUtc = period.FromUtc;
        var effectiveToUtcExclusive = period.ToUtcExclusive;
        int? bucketIndex = null;

        if (request.BucketIndex.HasValue)
        {
            var requestedBucket = request.BucketIndex.Value;
            if (requestedBucket < 0 || requestedBucket >= period.BucketCount)
                throw new ArgumentOutOfRangeException(nameof(request.BucketIndex));

            bucketIndex = requestedBucket;
            effectiveFromUtc = period.FromUtc.AddHours(
                (long)requestedBucket * period.BucketHours);
            effectiveToUtcExclusive = effectiveFromUtc.AddHours(period.BucketHours);
            if (effectiveToUtcExclusive > period.ToUtcExclusive)
                effectiveToUtcExclusive = period.ToUtcExclusive;
        }

        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 10, 100);
        var search = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim();
        if (search is { Length: > 100 })
            throw new ArgumentOutOfRangeException(nameof(request.Search), "Từ khóa tìm kiếm tối đa 100 ký tự.");

        return new SalesResolvedDetailQueryDto
        {
            Period = period,
            EffectiveFromUtc = effectiveFromUtc,
            EffectiveToUtcExclusive = effectiveToUtcExclusive,
            TerminalId = periodSet.Current.TerminalId,
            CustomerState = periodSet.Current.CustomerState,
            Search = search,
            Page = page,
            PageSize = pageSize,
            Sort = string.IsNullOrWhiteSpace(request.Sort)
                ? SalesDetailSorts.Newest
                : request.Sort.Trim().ToLowerInvariant(),
            VariantId = request.VariantId is > 0 ? request.VariantId : null,
            BucketIndex = bucketIndex,
            Focus = string.IsNullOrWhiteSpace(request.Focus) ? null : request.Focus.Trim().ToLowerInvariant()
        };
    }

    public DateTime ConvertUtcToLocal(DateTime utcDateTime)
        => TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utcDateTime), _timeZone);

    private SalesReportPeriodDto BuildPeriod(
        DateTime fromDate,
        DateTime toDate)
    {
        var dayCount = (toDate - fromDate).Days + 1;

        var (granularity, bucketHours) = dayCount switch
        {
            1 => (SalesTrendGranularities.Hour, 1),
            <= 60 => (SalesTrendGranularities.Day, 24),
            _ => (SalesTrendGranularities.Week, 168)
        };

        var fromUtc = LocalDateToUtc(fromDate);
        var toUtcExclusive = LocalDateToUtc(toDate.AddDays(1));
        var totalHours = (toUtcExclusive - fromUtc).TotalHours;
        var bucketCount = Math.Max(
            1,
            (int)Math.Ceiling(totalHours / bucketHours));

        return new SalesReportPeriodDto
        {
            FromDate = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Unspecified),
            ToDate = DateTime.SpecifyKind(toDate.Date, DateTimeKind.Unspecified),
            FromUtc = fromUtc,
            ToUtcExclusive = toUtcExclusive,
            Granularity = granularity,
            BucketHours = bucketHours,
            BucketCount = bucketCount
        };
    }

    private DateTime LocalDateToUtc(DateTime localDate)
    {
        var localMidnight = DateTime.SpecifyKind(
            localDate.Date,
            DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, _timeZone);
    }

    private static string NormalizeComparisonMode(string? value)
        => string.Equals(
            value?.Trim(),
            SalesComparisonModes.None,
            StringComparison.OrdinalIgnoreCase)
                ? SalesComparisonModes.None
                : SalesComparisonModes.PreviousPeriod;

    private static string NormalizeCustomerState(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            SalesCustomerStates.Linked => SalesCustomerStates.Linked,
            SalesCustomerStates.Guest => SalesCustomerStates.Guest,
            _ => SalesCustomerStates.All
        };

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(CanonicalTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallbackTimeZoneId);
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsFallbackTimeZoneId);
        }
    }
}
