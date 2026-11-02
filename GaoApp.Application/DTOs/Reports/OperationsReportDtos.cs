using GaoApp.Application.DTOs.Reports.Sales;
using GaoApp.Domain.Entities;

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
    decimal Amount, string? Reference, int? ManualId = null, string? RowVersion = null, bool CanReverse = false, bool IsTransfer = false,
    BankTimePrecision? TimePrecision = null, DateTime? BankBusinessDate = null)
{
    public string AmountText => ReportMoneyText.Format(Amount);
    public string InflowText => ReportMoneyText.Format(Math.Max(0,Amount));
    public string OutflowText => ReportMoneyText.Format(Math.Max(0,-Amount));
    public string? TimeText => TimePrecision==BankTimePrecision.BankDay?BankBusinessDate?.ToString("dd/MM/yyyy",System.Globalization.CultureInfo.InvariantCulture)+" (ngày ngân hàng)":null;
}
public sealed record FundSummaryDto(string Fund, string Name, decimal? Opening, decimal Inflow, decimal Outflow,
    decimal Net, decimal? Closing, DateTime? BaselineDate)
{
    public string OpeningText=>ReportMoneyText.Format(Opening);public string InflowText=>ReportMoneyText.Format(Inflow);
    public string OutflowText=>ReportMoneyText.Format(Outflow);public string NetText=>ReportMoneyText.Format(Net);public string ClosingText=>ReportMoneyText.Format(Closing);
}
public sealed record CashTrendDto(string Label, decimal Inflow, decimal Outflow, decimal Net)
{
    public string InflowText=>ReportMoneyText.Format(Inflow);public string OutflowText=>ReportMoneyText.Format(Outflow);public string NetText=>ReportMoneyText.Format(Net);
}
public sealed record BankEvidenceReviewDto(string Id,decimal Amount,string Reason,string? BankDay)
{public string AmountText=>ReportMoneyText.Format(Amount);}
public sealed record CashFlowReportDto(SalesReportPeriodDto Period, IReadOnlyList<ReportOptionDto> Funds,
    IReadOnlyList<FundSummaryDto> Summary, IReadOnlyList<CashTrendDto> Trend, IReadOnlyList<MoneyMovementDto> Items,
    int Count, IReadOnlyList<string> Warnings, decimal ClosingShiftDifference,IReadOnlyList<BankEvidenceReviewDto>? BankReceiptReviews=null)
{
    private IEnumerable<MoneyMovementDto> External=>Items.Where(x=>!x.IsTransfer&&x.Source!="Phiếu tiền mặt ca");
    public string ExternalInflowText=>ReportMoneyText.Format(External.Sum(x=>Math.Max(0,x.Amount)));
    public string ExternalOutflowText=>ReportMoneyText.Format(External.Sum(x=>Math.Max(0,-x.Amount)));
    public string ExternalNetText=>ReportMoneyText.Format(External.Sum(x=>x.Amount));
    public string SelectedClosingText=>ReportMoneyText.Format(Summary.Where(x=>x.Fund!="other").Any()&&Summary.Where(x=>x.Fund!="other").All(x=>x.Closing.HasValue)?Summary.Where(x=>x.Fund!="other").Sum(x=>x.Closing):null);
}
internal static class ReportMoneyText
{internal static string Format(decimal? value)=>value?.ToString("#,0.###",System.Globalization.CultureInfo.GetCultureInfo("vi-VN"))??"Chưa xác định";}
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
