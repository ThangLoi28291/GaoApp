namespace GaoApp.Application.DTOs.Rewards;

/// <summary>
/// Số dư tích điểm của khách hàng.
/// Amount là tiền tích lũy hợp lệ, không phải điểm.
/// </summary>
public sealed class CustomerRewardBalanceDto
{
    public int CustomerId { get; set; }

    public decimal BalanceAmount { get; set; }

    public decimal MoneyPerPoint { get; set; }

    public int PointsPerVoucher { get; set; }

    public decimal VoucherValue { get; set; }

    public decimal AvailablePoints =>
        MoneyPerPoint <= 0 ? 0 : Math.Floor(BalanceAmount / MoneyPerPoint);

    public int AvailableVoucherCount =>
        PointsPerVoucher <= 0 ? 0 : (int)(AvailablePoints / PointsPerVoucher);

    public decimal RequiredAmountPerVoucher =>
        MoneyPerPoint * PointsPerVoucher;
}