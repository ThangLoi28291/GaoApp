using GaoApp.Application.DTOs.Rewards;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.POS;

public sealed class OrderDraftDto
{
    public int OrderId { get; set; }
    public string? OrderNumber { get; set; }

    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerPriceTier { get; set; }
    public CustomerRewardSummaryDto? RewardSummary { get; set; }
    public string? Note { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal OrderDiscount { get; set; }
    public decimal GrandTotal { get; set; }

    public decimal PaidTotal { get; set; }
    public decimal BalanceDue { get; set; }
    public decimal ChangeDue { get; set; }
    public decimal VoucherDiscountTotal { get; set; }
    public decimal PromotionDiscountTotal { get; set; }
    public decimal ComboDiscountTotal { get; set; }

    public int? ComboPromotionId { get; set; }

    public string? ComboPromotionName { get; set; }

    public string? ComboPromotionNote { get; set; }

    public List<AppliedRewardVoucherDto> AppliedRewardVouchers { get; set; } = new();

    public List<OrderPaymentDto> Payments { get; set; } = new();
    public List<OrderLineDto> Lines { get; set; } = new();
}