namespace GaoApp.Application.DTOs.POS;

public sealed class OrderRewardVoucherDto
{
    public int VoucherId { get; set; }

    public string VoucherCode { get; set; } = string.Empty;

    public decimal Value { get; set; }

    public string? Status { get; set; }
}