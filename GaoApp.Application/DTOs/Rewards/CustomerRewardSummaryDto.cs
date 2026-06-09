namespace GaoApp.Application.DTOs.Rewards;

public sealed class CustomerRewardSummaryDto
{
    public int CustomerId { get; set; }

    public decimal BalanceAmount { get; set; }
    public decimal AvailablePoints { get; set; }

    public int RedeemableVoucherCount { get; set; }
    public decimal RedeemableVoucherValue { get; set; }

    public int AvailableVoucherCount { get; set; }
    public decimal AvailableVoucherValue { get; set; }

    public decimal MoneyPerPoint { get; set; }
    public int PointsPerVoucher { get; set; }
    public decimal VoucherValue { get; set; }
}