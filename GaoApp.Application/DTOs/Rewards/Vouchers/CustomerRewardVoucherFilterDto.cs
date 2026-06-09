using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public sealed class CustomerRewardVoucherFilterDto
{
    public string? Keyword { get; set; }
    public int? CustomerId { get; set; }
    public CustomerRewardVoucherStatus? Status { get; set; }

    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}