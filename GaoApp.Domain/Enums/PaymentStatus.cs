namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái thanh toán của đơn hàng
/// </summary>
public enum PaymentStatus
{
    /// <summary>
    /// Chưa thanh toán
    /// </summary>
    Unpaid = 0,

    /// <summary>
    /// Đã thanh toán một phần (chưa đủ tiền)
    /// </summary>
    PartiallyPaid = 1,

    /// <summary>
    /// Đã thanh toán đủ
    /// </summary>
    Paid = 2,

    /// <summary>
    /// Thanh toán của đơn đã bị hủy sau khi chốt
    /// </summary>
    Voided = 3,

    /// <summary>
    /// Thanh toán của đơn đã được hoàn tiền / trả hàng
    /// </summary>
    Refunded = 4
}