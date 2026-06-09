namespace GaoApp.Application.DTOs.Rewards;

public sealed class CustomerRewardVoucherDto
{
    public int Id { get; set; }
    public string VoucherCode { get; set; } = default!;
    public decimal Value { get; set; }
    public decimal RequiredAmount { get; set; }
    public string Status { get; set; } = default!;
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public string? Description { get; set; }
}