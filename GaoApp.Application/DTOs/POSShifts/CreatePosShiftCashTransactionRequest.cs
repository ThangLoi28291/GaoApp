using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POSShifts;

public class CreatePosShiftCashTransactionRequest
{
    /// <summary>
    /// Loại giao dịch tiền mặt: CashIn hoặc CashOut.
    /// </summary>
    public POSShiftCashTransactionType Type { get; set; }

    /// <summary>
    /// Số tiền thu hoặc chi.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Ghi chú bổ sung, không bắt buộc.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Lý do chính của giao dịch thu hoặc chi tiền mặt.
    /// </summary>
    [Required(ErrorMessage = "Vui lòng nhập lý do thu/chi tiền mặt.")]
    [StringLength(
        300,
        ErrorMessage = "Lý do không được vượt quá 300 ký tự.")]
    public string Reason { get; set; } = string.Empty;
}