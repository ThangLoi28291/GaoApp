namespace GaoApp.Application.DTOs.POS;

/// <summary>
/// Request dùng để thực hiện trả hàng / hoàn tiền toàn bộ đơn
/// </summary>
public sealed class RefundOrderRequest
{
    /// <summary>
    /// Lý do trả hàng / hoàn tiền
    /// Bắt buộc nhập
    /// </summary>
    public string Reason { get; set; } = default!;
}