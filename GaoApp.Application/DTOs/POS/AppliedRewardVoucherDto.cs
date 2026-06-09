namespace GaoApp.Application.DTOs.POS;

/// <summary>
/// Voucher đã áp dụng vào đơn nháp.
/// </summary>
public sealed class AppliedRewardVoucherDto
{
    public int VoucherId { get; set; }

    public string VoucherCode { get; set; } = string.Empty;

    public decimal Value { get; set; }
}