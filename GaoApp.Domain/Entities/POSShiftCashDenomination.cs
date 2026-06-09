using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Chi tiết kiểm đếm tiền theo mệnh giá của ca POS.
/// 
/// Dùng cho:
/// - Nhận ca: lưu số tờ từng mệnh giá lúc mở ca.
/// - Đóng ca: lưu số tờ từng mệnh giá lúc bàn giao cuối ca.
/// - In phiếu 80mm có barcode sau này.
/// </summary>
[Table("POSShiftCashDenominations")]
public class POSShiftCashDenomination : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Ca POS liên quan.
    /// </summary>
    public int POSShiftId { get; set; }

    public POSShift POSShift { get; set; } = default!;

    /// <summary>
    /// Opening = tiền đầu ca.
    /// Closing = tiền cuối ca.
    /// </summary>
    public POSShiftCashDenominationEntryType EntryType { get; set; }

    /// <summary>
    /// Mệnh giá tiền.
    /// Ví dụ: 500000, 200000, 100000...
    /// </summary>
    public int DenominationValue { get; set; }

    /// <summary>
    /// Số tờ / số lượng.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Thành tiền = DenominationValue * Quantity.
    /// Lưu snapshot để in phiếu và đối chiếu nhanh.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Ghi chú nếu cần.
    /// </summary>
    public string? Note { get; set; }

    public void Recalc()
    {
        if (DenominationValue < 0)
            DenominationValue = 0;

        if (Quantity < 0)
            Quantity = 0;

        Amount = DenominationValue * Quantity;
    }
}