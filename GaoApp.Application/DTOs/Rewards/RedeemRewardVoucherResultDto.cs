namespace GaoApp.Application.DTOs.Rewards;

public sealed class RedeemRewardVoucherResultDto
{
    public int CustomerId { get; set; }

    public int CreatedVoucherCount { get; set; }

    public decimal DeductedAmount { get; set; }

    public decimal VoucherValue { get; set; }

    public CustomerRewardBalanceDto Balance { get; set; } = default!;

    public List<CustomerRewardVoucherDto> CreatedVouchers { get; set; } = new();
}