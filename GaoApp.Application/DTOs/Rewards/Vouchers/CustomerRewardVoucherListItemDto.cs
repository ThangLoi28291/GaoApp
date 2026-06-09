namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public class CustomerRewardVoucherListItemDto
{
    public int Id { get; set; }
    public string VoucherCode { get; set; } = string.Empty;

    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }

    public decimal Value { get; set; }
    public decimal RequiredAmount { get; set; }

    public string Status { get; set; } = string.Empty;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }

    public int? UsedOrderId { get; set; }
    public string? Description { get; set; }
}