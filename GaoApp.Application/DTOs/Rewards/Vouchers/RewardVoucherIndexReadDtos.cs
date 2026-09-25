using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Rewards.Vouchers;

public static class RewardVoucherIndexStatuses
{
    public const string All = "all";
    public const string Available = "available";
    public const string Used = "used";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string Locked = "locked";
}

public sealed class RewardVoucherIndexQueryRequest
{
    public string? Keyword { get; set; }
    public string? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class RewardVoucherIndexPageDto
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalItems / Math.Max(1, PageSize)));
    public RewardVoucherIndexSummaryDto Summary { get; set; } = new();
    public List<RewardVoucherIndexItemDto> Items { get; set; } = new();
}

public sealed class RewardVoucherIndexSummaryDto
{
    public int TotalVouchers { get; set; }
    public int AvailableVouchers { get; set; }
    public int UsedVouchers { get; set; }
    public int UnavailableVouchers { get; set; }
}

public sealed class RewardVoucherIndexItemDto
{
    public int VoucherId { get; set; }
    public string VoucherCode { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerCode { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public decimal Value { get; set; }
    public decimal RequiredAmount { get; set; }
    public CustomerRewardVoucherStatus Status { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public bool IsLinkedToOrder { get; set; }
}

public sealed class RewardVoucherIndexQuickViewDto
{
    public int VoucherId { get; set; }
    public string VoucherCode { get; set; } = string.Empty;
    public string? ReferenceCode { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerCode { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerEmail { get; set; }
    public decimal Value { get; set; }
    public decimal RequiredAmount { get; set; }
    public CustomerRewardVoucherStatus Status { get; set; }
    public DateTime IssuedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public bool IsLinkedToOrder { get; set; }
    public string? Description { get; set; }
}
