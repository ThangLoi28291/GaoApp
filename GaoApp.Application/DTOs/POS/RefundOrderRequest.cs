using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POS;

/// <summary>
/// Request dùng để thực hiện trả hàng / hoàn tiền toàn bộ đơn
/// </summary>
public sealed class RefundOrderRequest
{
    /// <summary>
    /// Lý do trả hàng / hoàn tiền.
    /// Bắt buộc nhập.
    /// </summary>
    public string Reason { get; set; } = default!;

    /// <summary>
    /// Phương thức hoàn tiền thực tế.
    /// Ví dụ:
    /// - Đơn gốc khách chuyển khoản
    /// - Nhưng nhân viên trả lại tiền mặt
    /// => RefundMethod = Cash
    /// </summary>
    public PaymentMethod RefundMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>
    /// Mã giao dịch hoàn tiền nếu hoàn qua chuyển khoản / thẻ / ví.
    /// </summary>
    public string? RefundReferenceCode { get; set; }

    /// <summary>
    /// Nhà cung cấp / ngân hàng / kênh hoàn tiền.
    /// Ví dụ: ACB, VietQR, Thẻ, Momo...
    /// </summary>
    public string? RefundProvider { get; set; }
}