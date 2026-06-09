namespace GaoApp.Application.DTOs.Rewards;

public sealed class RedeemRewardVoucherRequest
{
    public int CustomerId { get; set; }

    // Số phiếu muốn đổi. Mặc định 1.
    public int VoucherCount { get; set; } = 1;

    public string? Description { get; set; }
}