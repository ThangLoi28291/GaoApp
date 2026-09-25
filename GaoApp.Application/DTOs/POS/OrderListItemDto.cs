namespace GaoApp.Application.DTOs.POS;

public sealed class OrderListItemDto
{
    public int OrderId { get; set; }
    public bool HasBankTransfer { get; set; }
    public string? OrderNumber { get; set; }

    public string Status { get; set; } = default!;
    public string PaymentStatus { get; set; } = default!;

    public decimal GrandTotal { get; set; }
    public decimal PaidTotal { get; set; }
    public decimal BalanceDue { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>
    /// Tổng tiền đã hoàn từ các phiếu return/refund completed của order này.
    /// </summary>
    public decimal RefundedTotal { get; set; }

    /// <summary>
    /// Số phiếu hậu mãi completed của order này.
    /// </summary>
    public int ReturnCount { get; set; }

    /// <summary>
    /// Cờ tiện dùng cho UI.
    /// </summary>
    public bool HasAfterSale => ReturnCount > 0 || RefundedTotal > 0;
    public decimal VoucherDiscountTotal { get; set; }

    public List<OrderRewardVoucherDto> RewardVouchers { get; set; } = new();

    public bool HasVoucher => VoucherDiscountTotal > 0 || RewardVouchers.Count > 0;
}