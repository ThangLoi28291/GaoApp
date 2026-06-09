namespace GaoApp.Application.DTOs.POS;

/// <summary>
/// Request áp dụng voucher tích điểm vào giỏ POS hiện tại.
/// </summary>
public sealed class ApplyRewardVouchersRequest
{
    public List<int> VoucherIds { get; set; } = new();
}