using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.DTOs.Reports.Profit;

public enum ProfitQuality { Empty, Finalized, Provisional, Unavailable, DataIntegrityConflict }

public sealed record ProfitMetricDto(decimal? Value, ProfitQuality Quality)
{
    public bool Available => Value.HasValue;
}

public sealed class ProfitReportQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Compare { get; set; } = "previous";
    public int? TerminalId { get; set; }
    public string? CustomerState { get; set; } = "all";
    public string? Segment { get; set; } = "profit";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Search { get; set; }
    public string? Sort { get; set; } = "newest";
    public int? Bucket { get; set; }
}

public sealed class ProfitSummaryDto
{
    public ProfitMetricDto NetSales { get; set; } = new(null, ProfitQuality.Unavailable);
    public ProfitMetricDto Cogs { get; set; } = new(null, ProfitQuality.Unavailable);
    public ProfitMetricDto GrossProfit { get; set; } = new(null, ProfitQuality.Unavailable);
    public ProfitMetricDto GrossMargin { get; set; } = new(null, ProfitQuality.Unavailable);
    public ProfitMetricDto ProvisionalCogs { get; set; } = new(null, ProfitQuality.Unavailable);
    public string CostState { get; set; } = "Chưa đủ dữ liệu";
    public int? AffectedOrders { get; set; }
    public int? AffectedLines { get; set; }
    public bool IsEmpty { get; set; }
    public string? Message { get; set; }
}

public sealed record ProfitTrendPointDto(int Bucket, string Label, ProfitSummaryDto Summary);
public sealed record ProfitDetailRowDto(int OrderId, string OrderNumber, DateTime? SaleDate,
    DateTime EventDate, string EventKind, ProfitSummaryDto Summary, int? OrderLineId = null, string? ItemName = null,
    int? SalesReturnId = null);
public sealed record ProfitActivityRowDto(int EntryId, int? OrderId, string OrderNumber,
    DateTime? SaleDate, DateTime AdjustmentDate, decimal? Impact, ProfitQuality Quality);
public sealed class ProfitActivityDto
{
    public List<ProfitActivityDayDto> Days { get; set; } = [];
    public decimal? Increase { get; set; }
    public decimal? Decrease { get; set; }
    public decimal? Net { get; set; }
    public int? Count { get; set; }
    public ProfitQuality Quality { get; set; }
    public List<ProfitActivityRowDto> Rows { get; set; } = [];
}
public sealed record ProfitActivityDayDto(DateTime Date, decimal? Increase, decimal? Decrease, ProfitQuality Quality);
public sealed class ProfitReportResponseDto
{
    public string Generation { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime GeneratedAtUtc { get; set; }
    public ProfitReportQueryDto Query { get; set; } = new();
    public SalesReportPeriodDto Period { get; set; } = new();
    public SalesReportPeriodDto? ComparisonPeriod { get; set; }
    public ProfitSummaryDto Current { get; set; } = new();
    public ProfitSummaryDto? Comparison { get; set; }
    public List<ProfitTrendPointDto> Trend { get; set; } = [];
    public List<ProfitTrendPointDto> ComparisonTrend { get; set; } = [];
    public List<ProfitDetailRowDto> Details { get; set; } = [];
    public int TotalItems { get; set; }
    public ProfitActivityDto? Activity { get; set; }
    public List<SalesTerminalOptionDto> Terminals { get; set; } = [];
}

// Detached evidence transferred only between repository and application. Never serialized by controllers.
public sealed class ProfitSourceSnapshot
{
    public int StoreId { get; init; }
    public DateTime ReadAtUtc { get; init; }
    public List<Order> Orders { get; set; } = [];
    public List<OrderLine> Lines { get; set; } = [];
    public List<SalesReturn> Returns { get; set; } = [];
    public List<SalesReturnLine> ReturnLines { get; set; } = [];
    public List<POSShift> Shifts { get; set; } = [];
    public List<InventoryValuationEntry> Entries { get; set; } = [];
    public List<OrderLegalEntityAllocation> LegalAllocations { get; set; } = [];
    public List<OrderLegalEntityAllocationReversal> LegalReversals { get; set; } = [];
    public List<LegalEntity> LegalEntities { get; set; } = [];
    public List<SalesTerminalOptionDto> Terminals { get; set; } = [];
    public HashSet<int> ActivityEntryIds { get; set; } = [];
}
