namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public sealed class CustomerRewardVoucherLogDto
{
    public DateTime CreatedAtUtc { get; set; }

    public string Action { get; set; } = default!;

    public int? OrderId { get; set; }

    public string? Reason { get; set; }

    public string? Note { get; set; }
}