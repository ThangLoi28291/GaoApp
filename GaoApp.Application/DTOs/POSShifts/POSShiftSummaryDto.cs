using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public class POSShiftSummaryDto
{
    public int Id { get; set; }
    public string? ShiftCode { get; set; }
    public POSShiftStatus Status { get; set; }

    public int OpenedByUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public int? ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public decimal OpeningCash { get; set; }
    public decimal CashSalesTotal { get; set; }
    public decimal NonCashSalesTotal { get; set; }

    public decimal CashRefundTotal { get; set; }
    public decimal NonCashRefundTotal { get; set; }
    public decimal RefundTotal { get; set; }
    public int RefundCount { get; set; }
    public int VoidCount { get; set; }

    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }
    public decimal ClosingCashExpected { get; set; }
    public decimal? ClosingCashActual { get; set; }

    public string? OpenNote { get; set; }
    public string? CloseNote { get; set; }

    public int TotalOrders { get; set; }
    public int DraftOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int CancelledOrders { get; set; }

    public decimal CompletedSalesTotal { get; set; }

    public List<POSShiftSummaryOrderDto> Orders { get; set; } = new();
    public List<POSShiftCashTransactionDto> CashTransactions { get; set; } = new();
}