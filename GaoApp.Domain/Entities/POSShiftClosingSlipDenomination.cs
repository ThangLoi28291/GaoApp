using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Chi tiết mệnh giá tiền thực đếm cuối ca.
/// </summary>
[Table("POSShiftClosingSlipDenominations")]
public class POSShiftClosingSlipDenomination : BaseStoreEntity, IAuditTrackedEntity
{
    public int POSShiftClosingSlipId { get; set; }
    public POSShiftClosingSlip POSShiftClosingSlip { get; set; } = default!;

    public int DenominationValue { get; set; }
    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    public void Recalc()
    {
        if (DenominationValue < 0) DenominationValue = 0;
        if (Quantity < 0) Quantity = 0;

        Amount = DenominationValue * Quantity;
    }
}