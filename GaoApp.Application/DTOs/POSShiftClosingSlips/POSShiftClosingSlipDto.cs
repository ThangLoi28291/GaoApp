using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShiftClosingSlips;

public class POSShiftClosingSlipDto
{
    public int Id { get; set; }
    public int POSShiftId { get; set; }

    public string SlipCode { get; set; } = default!;
    public string BarcodeValue { get; set; } = default!;
    public POSShiftClosingSlipStatus Status { get; set; }

    public string? ShiftCode { get; set; }

    public int OpenedByUserId { get; set; }
    public string? OpenedByUserName { get; set; }

    public int ClosedByUserId { get; set; }
    public string? ClosedByUserName { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime ClosedAtUtc { get; set; }

    public decimal OpeningCash { get; set; }
    public decimal CashSalesTotal { get; set; }
    public decimal NonCashSalesTotal { get; set; }

    public decimal CashRefundTotal { get; set; }
    public decimal NonCashRefundTotal { get; set; }
    public int RefundCount { get; set; }
    public int VoidCount { get; set; }

    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }

    public decimal ClosingCashExpected { get; set; }
    public decimal ClosingCashActual { get; set; }
    public decimal CashDifference { get; set; }

    public string? CloseNote { get; set; }

    public DateTime? PrintedAtUtc { get; set; }

    public List<POSShiftClosingSlipDenominationDto> Denominations { get; set; } = new();
}

public class POSShiftClosingSlipDenominationDto
{
    public int DenominationValue { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}