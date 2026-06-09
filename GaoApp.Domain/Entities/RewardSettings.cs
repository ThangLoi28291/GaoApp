using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("RewardSettings")]
public class RewardSettings : BaseStoreEntity
{
    /// <summary>
    /// Bao nhiêu tiền tích lũy được 1 điểm.
    /// Ví dụ 65.000
    /// </summary>
    public decimal MoneyPerPoint { get; set; }

    /// <summary>
    /// Bao nhiêu điểm đổi được 1 phiếu.
    /// Ví dụ 30 điểm
    /// </summary>
    public int PointsPerVoucher { get; set; }

    /// <summary>
    /// Giá trị phiếu giảm giá.
    /// Ví dụ 30.000
    /// </summary>
    public decimal VoucherValue { get; set; }

    /// <summary>
    /// Có bật tích điểm không.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Ghi chú.
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }
}