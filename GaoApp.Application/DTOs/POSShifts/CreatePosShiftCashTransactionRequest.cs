using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public class CreatePosShiftCashTransactionRequest
{
    /// <summary>
    /// CashIn / CashOut
    /// </summary>
    public POSShiftCashTransactionType Type { get; set; }

    /// <summary>
    /// Số tiền thu/chi
    /// </summary>
    public decimal Amount { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// Ghi chú
    /// </summary>
    public string Reason { get; set; }
}