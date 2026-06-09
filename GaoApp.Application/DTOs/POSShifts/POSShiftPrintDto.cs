using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

/// <summary>
/// DTO dùng để in phiếu ca POS khổ 80mm.
/// Dùng chung cho:
/// - Phiếu nhận ca
/// - Phiếu đóng ca / bàn giao ca
/// </summary>
public class POSShiftPrintDto
{
    public int ShiftId { get; set; }
    public string? ShiftCode { get; set; }
    public POSShiftStatus Status { get; set; }

    public string PrintType { get; set; } = "Opening"; // Opening / Closing
    public string BarcodeValue { get; set; } = default!;

    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public int OpenedByUserId { get; set; }
    public string? OpenedByUserName { get; set; }

    public int? ClosedByUserId { get; set; }
    public string? ClosedByUserName { get; set; }

    public int TerminalId { get; set; }
    public string? TerminalCode { get; set; }
    public string? TerminalName { get; set; }

    public int WarehouseId { get; set; }
    public string? WarehouseCode { get; set; }
    public string? WarehouseName { get; set; }

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
    public decimal? CashDifference { get; set; }

    public string? OpenNote { get; set; }
    public string? CloseNote { get; set; }

    public List<POSShiftPrintDenominationDto> Denominations { get; set; } = new();
}

public class POSShiftPrintDenominationDto
{
    public int DenominationValue { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}