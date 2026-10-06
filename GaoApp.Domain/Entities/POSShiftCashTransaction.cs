using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Phiếu thu / chi tiền mặt phát sinh trong ca.
/// Không phải payment của order.
/// Đây là nghiệp vụ tác động trực tiếp đến két tiền.
/// </summary>
[Table("POSShiftCashTransactions")]
public class POSShiftCashTransaction : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// FK đến ca POS.
    /// </summary>
    public int POSShiftId { get; set; }

    public POSShift POSShift { get; set; } = default!;

    /// <summary>
    /// Loại giao dịch:
    /// - CashIn: nộp thêm vào két
    /// - CashOut: rút khỏi két
    /// </summary>
    public POSShiftCashTransactionType Type { get; set; }

    /// <summary>
    /// Số tiền giao dịch.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// Lý do chính.
    /// Ví dụ:
    /// - Bổ sung tiền lẻ
    /// - Rút tiền nộp két lớn
    /// - Chi mua văn phòng phẩm
    /// </summary>
    [Required]
    [StringLength(300)]
    public string Reason { get; set; } = default!;

    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Người tạo phiếu.
    /// </summary>
    public int CreatedByUserId { get; set; }
    public int? CustomerDepositEntryId { get; set; }
    public int? CustomerDebtReceiptId { get; set; }
}
