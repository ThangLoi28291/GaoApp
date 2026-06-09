namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public sealed class CustomerRewardVoucherPrintDto
{
    public int Id { get; set; }

    public string VoucherCode { get; set; } = default!;

    public string CustomerName { get; set; } = default!;

    public string? CustomerPhone { get; set; }

    public decimal Value { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    public DateTime? ExpiredAtUtc { get; set; }

    public string Status { get; set; } = default!;

    public string? Description { get; set; }
}