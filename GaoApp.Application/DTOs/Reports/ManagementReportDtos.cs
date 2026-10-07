using GaoApp.Application.DTOs.Reports.Profit;
using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.DTOs.Reports;

public sealed class ManagementReportQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Compare { get; set; } = "previous";
    public int? TerminalId { get; set; }
    public string? CustomerState { get; set; } = "all";
}
public sealed record ReportCustomerInfo(int Id, string Name, string PriceTier);
public sealed record ReportProductInfo(int VariantId, string Name, string? Sku, int CategoryId, string CategoryName);
public sealed record ReportDimensionDto(int Id, string Name, string? Group, string? Sku,
    decimal BaseQuantity, int SalesOrders, ProfitSummaryDto Summary);
public sealed record ReportGroupTrendDto(int Bucket, string Label, decimal? Wholesale, decimal? Retail, decimal? Unknown);
public sealed record ExpenseCategoryDto(string Code, string Name, decimal Amount);
public sealed class ManagementPeriodDto
{
    public ProfitSummaryDto Summary { get; set; } = new();
    public decimal OperatingExpenses { get; set; }
    public decimal? OperatingProfit { get; set; }
    public int DraftExpenses { get; set; }
    public int InferredCustomerOrders { get; set; }
    public int SalesOrders { get; set; }
    public List<ProfitTrendPointDto> Trend { get; set; } = [];
    public List<ReportGroupTrendDto> GroupTrend { get; set; } = [];
    public List<decimal> ExpenseTrend { get; set; } = [];
    public List<ReportDimensionDto> Products { get; set; } = [];
    public List<ReportDimensionDto> Categories { get; set; } = [];
    public List<ReportDimensionDto> Customers { get; set; } = [];
    public List<ReportDimensionDto> CustomerGroups { get; set; } = [];
    public List<ExpenseCategoryDto> ExpenseCategories { get; set; } = [];
}
public sealed class ManagementReportDto
{
    public DateTime GeneratedAtUtc { get; set; }
    public SalesReportPeriodDto Period { get; set; } = new();
    public SalesReportPeriodDto? ComparisonPeriod { get; set; }
    public ManagementPeriodDto Current { get; set; } = new();
    public ManagementPeriodDto? Comparison { get; set; }
    public List<SalesTerminalOptionDto> Terminals { get; set; } = [];
    public bool ExpensesAreStoreWide { get; set; } = true;
}
public sealed class ExpenseWriteDto
{
    public Guid ClientRequestId { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "other";
    public decimal Amount { get; set; }
    public DateTime RecognitionFrom { get; set; }
    public DateTime RecognitionTo { get; set; }
    public bool IsPaid { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public string? ReceiptReference { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class ExpenseActionDto
{
    public string RowVersion { get; set; } = "";
    public string? Reason { get; set; }
}
public sealed record ExpenseRowDto(int Id, Guid ClientRequestId, string Name, string Category, decimal Amount,
    DateTime RecognitionFrom, DateTime RecognitionTo, string Status, bool IsPaid, string PaymentMethod,
    string? ReceiptReference, string? Note, string RowVersion, decimal PeriodAmount, DateTime CreatedAtUtc, string? VoidReason);
public sealed record ExpenseListDto(List<ExpenseRowDto> Items, int TotalItems, int Page, int PageSize,
    decimal ConfirmedAmount, decimal DraftAmount);
