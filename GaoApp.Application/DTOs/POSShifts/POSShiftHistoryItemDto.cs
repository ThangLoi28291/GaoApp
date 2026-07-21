using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public class POSShiftHistoryItemDto
{
    public int Id { get; set; }
    public string? ShiftCode { get; set; }
    public POSShiftStatus Status { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public decimal OpeningCash { get; set; }
    public decimal CashSalesTotal { get; set; }
    public decimal NonCashSalesTotal { get; set; }
    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }
    public decimal ClosingCashExpected { get; set; }
    public decimal? ClosingCashActual { get; set; }

    public string? OpenNote { get; set; }
    public string? CloseNote { get; set; }
}