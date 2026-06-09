namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại phát sinh trong sổ tích điểm khách hàng.
/// </summary>
public enum CustomerRewardLedgerType
{
    /// <summary>
    /// Chuyển số dư tích lũy từ hệ thống cũ.
    /// </summary>
    ImportOldBalance = 1,

    /// <summary>
    /// Cộng tiền tích lũy từ đơn bán hàng.
    /// </summary>
    SaleEarned = 2,

    /// <summary>
    /// Trừ tiền tích lũy do khách trả hàng.
    /// </summary>
    ReturnDeducted = 3,

    /// <summary>
    /// Trừ tiền tích lũy khi đổi phiếu.
    /// </summary>
    VoucherRedeemed = 4,

    /// <summary>
    /// Điều chỉnh tay bởi admin.
    /// </summary>
    ManualAdjust = 5,
        /// <summary>
/// Trừ tích lũy do hủy đơn sau khi đã chốt.
/// </summary>
SaleVoided = 6,

    /// <summary>
    /// Trừ tích lũy do refund toàn phần đơn hàng.
    /// </summary>
    SaleRefunded = 7
}