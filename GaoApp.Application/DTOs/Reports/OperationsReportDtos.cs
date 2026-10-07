using GaoApp.Application.DTOs.Reports.Sales;

namespace GaoApp.Application.DTOs.Reports;

public sealed class OperationsReportQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? WarehouseId { get; set; }
    public string? Fund { get; set; }
    public decimal LowStockThreshold { get; set; } = 5;
}
public sealed record ReportOptionDto(string Id, string Name);
public sealed record MoneyMovementDto(string Id, DateTime AtUtc, string Fund, string Name, string Source,
    decimal Amount, string? Reference, int? ManualId = null, string? RowVersion = null, bool CanReverse = false, bool IsTransfer = false);
public sealed record FundSummaryDto(string Fund, string Name, decimal? Opening, decimal Inflow, decimal Outflow,
    decimal Net, decimal? Closing, DateTime? BaselineDate);
public sealed record CashTrendDto(string Label, decimal Inflow, decimal Outflow, decimal Net);
public sealed record CashFlowReportDto(SalesReportPeriodDto Period, IReadOnlyList<ReportOptionDto> Funds,
    IReadOnlyList<FundSummaryDto> Summary, IReadOnlyList<CashTrendDto> Trend, IReadOnlyList<MoneyMovementDto> Items,
    int Count, IReadOnlyList<string> Warnings, decimal ClosingShiftDifference);
public sealed record StockReportRowDto(int WarehouseId, int VariantId, string Warehouse, string Name, string Sku,
    string Category, string Unit, decimal? Opening, decimal Inbound, decimal Outbound, decimal? Closing,
    decimal? OpeningValue, decimal? ClosingValue, decimal Sold, DateTime? LastSoldAtUtc, decimal? DaysCover,
    string State, bool IsProvisional, bool IsIncomplete);
public sealed record StockReportDto(SalesReportPeriodDto Period, IReadOnlyList<ReportOptionDto> Warehouses,
    IReadOnlyList<StockReportRowDto> Items, decimal? InventoryValue, int NegativeCount, int LowCount,
    int SlowCount, int ProvisionalCount, bool CanViewCost, IReadOnlyList<string> Warnings);
public sealed record DebtReportRowDto(string Kind, int Id, int PartyId, string Party, string Document,
    DateTime AtUtc, DateTime? DueDate, decimal Opening, decimal Increase, decimal Decrease, decimal Closing,
    string Aging, string? RowVersion = null);
public sealed record DebtTrendDto(string Label, decimal NewDebt, decimal Settled);
public sealed record DebtReportDto(SalesReportPeriodDto Period, IReadOnlyList<DebtReportRowDto> Items,
    decimal Receivable, decimal Payable, decimal OverdueReceivable, decimal OverduePayable,
    IReadOnlyList<DebtTrendDto> Trend, IReadOnlyList<string> Warnings);

public sealed class TreasuryWriteDto
{
    public Guid ClientRequestId { get; set; }
    public string Fund { get; set; } = "cash";
    public string? TargetFund { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
    public string? Reference { get; set; }
    public string? Note { get; set; }
    public int? OperatingExpenseId { get; set; }
    public int? PurchasePayableId { get; set; }
    public int? POSShiftCashTransactionId { get; set; }
    public string? SourceRowVersion { get; set; }
    public bool ReconcileLegacyPayment { get; set; }
}
public sealed class TreasuryOpeningDto
{
    public string Fund { get; set; } = "cash";
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
}
public sealed class TreasuryReverseDto
{
    public Guid ClientRequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public DateTime Date { get; set; }
    public string Reason { get; set; } = "";
}
public sealed class PayableDueDateDto
{
    public string RowVersion { get; set; } = "";
    public DateTime? DueDate { get; set; }
}
public sealed record TreasurySourcesDto(IReadOnlyList<ReportOptionDto> Funds,
    IReadOnlyList<TreasurySourceDto> Expenses, IReadOnlyList<TreasurySourceDto> Payables,
    IReadOnlyList<TreasurySourceDto> CashVouchers);
public sealed record TreasurySourceDto(int Id, string Name, decimal Amount, string RowVersion, DateTime? Date = null, bool RequiresEvidence = false);
