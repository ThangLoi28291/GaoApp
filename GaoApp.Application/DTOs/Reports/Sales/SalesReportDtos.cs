namespace GaoApp.Application.DTOs.Reports.Sales;

public static class SalesComparisonModes
{
    public const string None = "none";
    public const string PreviousPeriod = "previous";
}

public static class SalesCustomerStates
{
    public const string All = "all";
    public const string Linked = "linked";
    public const string Guest = "guest";
}

public static class SalesTrendGranularities
{
    public const string Hour = "hour";
    public const string Day = "day";
    public const string Week = "week";
}

public static class SalesMetricDeltaStates
{
    public const string Up = "up";
    public const string Down = "down";
    public const string Flat = "flat";
    public const string New = "new";
}

public sealed class SalesExecutiveDashboardQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Compare { get; set; } = SalesComparisonModes.PreviousPeriod;
    public int? TerminalId { get; set; }
    public string? CustomerState { get; set; } = SalesCustomerStates.All;
}

public sealed class SalesReportPeriodDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateTime FromUtc { get; set; }
    public DateTime ToUtcExclusive { get; set; }
    public string Granularity { get; set; } = SalesTrendGranularities.Day;
    public int BucketHours { get; set; } = 24;
    public int BucketCount { get; set; }
}

public sealed class SalesResolvedExecutiveQueryDto
{
    public SalesReportPeriodDto Period { get; set; } = new();
    public int? TerminalId { get; set; }
    public string CustomerState { get; set; } = SalesCustomerStates.All;
}

public sealed class SalesResolvedPeriodSet
{
    public string ComparisonMode { get; set; } = SalesComparisonModes.PreviousPeriod;
    public SalesResolvedExecutiveQueryDto Current { get; set; } = new();
    public SalesResolvedExecutiveQueryDto? Comparison { get; set; }
}

public sealed class SalesMetricSummaryDto
{
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal SalesAfterDiscount { get; set; }
    public decimal Returns { get; set; }
    public decimal NetSales { get; set; }
    public decimal RefundAmount { get; set; }
    public int SalesOrders { get; set; }
    public decimal Aov { get; set; }
    public int ReturnCount { get; set; }
    public int RefundCount { get; set; }
    public int VoidCount { get; set; }
    public decimal VoidValue { get; set; }
    public int CustomerLinkedOrders { get; set; }
    public decimal CustomerLinkedRate { get; set; }
}

public sealed class SalesBridgeDto
{
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal SalesAfterDiscount { get; set; }
    public decimal Returns { get; set; }
    public decimal NetSales { get; set; }
}

public sealed class SalesTrendPointDto
{
    public int BucketIndex { get; set; }
    public DateTime BucketLocalStart { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal GrossSales { get; set; }
    public decimal Discounts { get; set; }
    public decimal SalesAfterDiscount { get; set; }
    public decimal Returns { get; set; }
    public decimal RefundAmount { get; set; }
    public decimal NetSales { get; set; }
    public int SalesOrders { get; set; }
    public int ReturnCount { get; set; }
    public int RefundCount { get; set; }
}

public sealed class SalesByHourPointDto
{
    public int Hour { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal SalesAfterDiscount { get; set; }
    public decimal Returns { get; set; }
    public decimal NetSales { get; set; }
    public int SalesOrders { get; set; }
}

public sealed class SalesTopProductDto
{
    public int VariantId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal BaseQuantity { get; set; }
    public decimal GrossSales { get; set; }
    public decimal GrossSalesShare { get; set; }
}

public sealed class SalesDiscountBreakdownDto
{
    public decimal ManualLine { get; set; }
    public decimal Promotion { get; set; }
    public decimal Combo { get; set; }
    public decimal ManualOrder { get; set; }
    public decimal Voucher { get; set; }
    public decimal TotalDiscounts { get; set; }
    public decimal ComponentTotal { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public bool IsReconciled { get; set; }
}

public sealed class SalesCustomerMixDto
{
    public int LinkedOrders { get; set; }
    public int GuestOrders { get; set; }
    public decimal LinkedRate { get; set; }
    public decimal GuestRate { get; set; }
}

public sealed class SalesExecutivePeriodDataDto
{
    public SalesMetricSummaryDto Summary { get; set; } = new();
    public SalesBridgeDto Bridge { get; set; } = new();
    public List<SalesTrendPointDto> Trend { get; set; } = new();
    public List<SalesByHourPointDto> SalesByHour { get; set; } = new();
    public List<SalesTopProductDto> TopProducts { get; set; } = new();
    public SalesDiscountBreakdownDto DiscountBreakdown { get; set; } = new();
    public SalesCustomerMixDto CustomerMix { get; set; } = new();
}

public sealed class SalesMetricDeltaDto
{
    public decimal Current { get; set; }
    public decimal Comparison { get; set; }
    public decimal Difference { get; set; }
    public decimal? PercentChange { get; set; }
    public string State { get; set; } = SalesMetricDeltaStates.Flat;
}

public sealed class SalesExecutiveComparisonsDto
{
    public SalesMetricDeltaDto NetSales { get; set; } = new();
    public SalesMetricDeltaDto SalesOrders { get; set; } = new();
    public SalesMetricDeltaDto Aov { get; set; } = new();
    public SalesMetricDeltaDto Discounts { get; set; } = new();
    public SalesMetricDeltaDto Returns { get; set; } = new();
    public SalesMetricDeltaDto RefundAmount { get; set; } = new();
}

public sealed class SalesTerminalOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class SalesExecutiveDashboardDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public SalesExecutiveDashboardQueryDto Query { get; set; } = new();
    public SalesReportPeriodDto CurrentPeriod { get; set; } = new();
    public SalesReportPeriodDto? ComparisonPeriod { get; set; }
    public SalesExecutivePeriodDataDto Current { get; set; } = new();
    public SalesExecutivePeriodDataDto? Comparison { get; set; }
    public SalesExecutiveComparisonsDto Comparisons { get; set; } = new();
    public List<SalesTerminalOptionDto> Terminals { get; set; } = new();
}


public static class SalesDetailSegments
{
    public const string Overview = "overview";
    public const string Orders = "orders";
    public const string Products = "products";
    public const string Discounts = "discounts";
    public const string Returns = "returns";
    public const string Void = "void";
}

public static class SalesDetailSorts
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string ValueDesc = "value-desc";
    public const string ValueAsc = "value-asc";
    public const string QuantityDesc = "quantity-desc";
    public const string NameAsc = "name-asc";
    public const string DiscountDesc = "discount-desc";
}

public sealed class SalesDetailQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? TerminalId { get; set; }
    public string? CustomerState { get; set; } = SalesCustomerStates.All;
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Sort { get; set; } = SalesDetailSorts.Newest;
    public int? VariantId { get; set; }
    public int? BucketIndex { get; set; }
    public string? Focus { get; set; }
}

public sealed class SalesResolvedDetailQueryDto
{
    public SalesReportPeriodDto Period { get; set; } = new();
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime EffectiveToUtcExclusive { get; set; }
    public int? TerminalId { get; set; }
    public string CustomerState { get; set; } = SalesCustomerStates.All;
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string Sort { get; set; } = SalesDetailSorts.Newest;
    public int? VariantId { get; set; }
    public int? BucketIndex { get; set; }
    public string? Focus { get; set; }
}

public sealed class SalesDetailContextDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public SalesDetailQueryDto Query { get; set; } = new();
    public SalesReportPeriodDto Period { get; set; } = new();
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime EffectiveToUtcExclusive { get; set; }
    public string? BucketLabel { get; set; }
    public List<SalesTerminalOptionDto> Terminals { get; set; } = new();
}

public sealed class SalesPagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
}

public sealed class SalesOrderDetailRowDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public DateTime CompletedAtLocal { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Discounts { get; set; }
    public decimal SalesAfterDiscount { get; set; }
    public int StatusCode { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public int ReturnCount { get; set; }
    public decimal ReturnValue { get; set; }
}

public sealed class SalesProductDetailRowDto
{
    public int VariantId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;
    public decimal BaseQuantity { get; set; }
    public decimal GrossSales { get; set; }
    public int SalesOrders { get; set; }
}

public sealed class SalesDiscountOrderRowDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public DateTime CompletedAtLocal { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal TotalDiscounts { get; set; }
    public decimal ManualLine { get; set; }
    public decimal Promotion { get; set; }
    public decimal Combo { get; set; }
    public decimal ManualOrder { get; set; }
    public decimal Voucher { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public bool IsReconciled { get; set; }
}

public sealed class SalesReturnDetailRowDto
{
    public int ReturnId { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public DateTime CompletedAtLocal { get; set; }
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int TypeCode { get; set; }
    public string TypeLabel { get; set; } = string.Empty;
    public decimal ReturnSubtotal { get; set; }
    public decimal RefundAmount { get; set; }
}

public sealed class SalesVoidDetailRowDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CompletedAtUtc { get; set; }
    public DateTime CompletedAtLocal { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal Discounts { get; set; }
    public decimal VoidValue { get; set; }
}
