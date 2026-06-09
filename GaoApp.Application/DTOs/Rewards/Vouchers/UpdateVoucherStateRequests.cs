namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public sealed class LockCustomerRewardVoucherRequest
{
    public string? Reason { get; set; }
}

public sealed class UnlockCustomerRewardVoucherRequest
{
    public string? Reason { get; set; }
}

public sealed class PrintCustomerRewardVoucherRequest
{
    public int VoucherId { get; set; }
}