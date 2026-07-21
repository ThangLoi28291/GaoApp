using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Chi tiết số tờ theo mệnh giá của phiếu nhận ca.
/// Tách riêng với POSShiftCashDenomination vì phiếu có thể tồn tại trước khi ca được mở.
/// </summary>
[Table("POSShiftHandoverSlipDenominations")]
public class POSShiftHandoverSlipDenomination : BaseStoreEntity, IAuditTrackedEntity
{
    public int POSShiftHandoverSlipId { get; set; }

    public POSShiftHandoverSlip POSShiftHandoverSlip { get; set; } = default!;

    /// <summary>
    /// Mệnh giá tiền.
    /// Ví dụ: 500000, 200000, 100000...
    /// </summary>
    public int DenominationValue { get; set; }

    /// <summary>
    /// Số tờ / số lượng.
    /// </summary>
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [StringLength(300)]
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